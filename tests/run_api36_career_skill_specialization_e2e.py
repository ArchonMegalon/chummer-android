#!/usr/bin/env python3
"""Prove one exact atomic Career skill-specialization purchase on an API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import uuid
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROLS = ("SkillControl.btnAddSpec",)
INVENTORY_ROW_IDS = (
    "Chummer/Controls/Skills/KnowledgeSkillControl.cs::"
    "Chummer.UI.Skills.KnowledgeSkillControl::btnAddSpec",
    "Chummer/Controls/Skills/SkillControl.cs::"
    "Chummer.UI.Skills.SkillControl::btnAddSpec",
)
INVENTORY_PHONE_STATUS_BEFORE = "partial_create_only"
INVENTORY_PHONE_STATUS_AFTER = "implemented_pending_emulator"
INVENTORY_PHONE_STATUS_COUNT_DELTA = {
    "implemented_pending_emulator": 2,
    "partial_create_only": -2,
}
SKILL_ID = "11111111-1111-1111-1111-111111111111"
SOURCE_SKILL_ID = "ae91a8a6-80e7-4f52-b9eb-21725a5528a4"
KNOWLEDGE_SKILL_ID = "33333333-3333-3333-3333-333333333333"
NULL_SOURCE_SKILL_ID = "00000000-0000-0000-0000-000000000000"
ORIGINAL_EXPENSE_ID = "22222222-2222-2222-2222-222222222222"
EXPECTED_SPECIALIZATION = "Bike"
CONTROL_PROOF_KEYS = (
    "typedActiveSkillIdentity",
    "typedCustomKnowledgeIdentityPreserved",
    "exactSourceSkillGuid",
    "sourceCatalogSelectionBound",
    "characterSourceRuleLogicalDigestsBound",
    "confirmationRequired",
    "karmaCostExact",
    "specializationXmlExact",
    "expenseUndoExact",
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
    device.wait("CareerSkillSpecializationE2E", timeout=120)
    device.wait("Continue building", timeout=120)
    authority = shared.read_workspace_authority(device)
    shared.require_import_authority(authority, fixture_sha256)
    return launch, authority


def open_page(device: shared.Device) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap(
        "build-career-skill-specialization",
        scroll=True,
        timeout=120,
        max_scrolls=45,
        scroll_distance_ratio=0.18,
    )
    device.wait("career-skill-specialization-page", timeout=60)


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


def root_for_authority(
    device: shared.Device,
    authority: shared.WorkspaceAuthority,
) -> ET.Element:
    matches = [
        payload
        for payload in workspace_payloads(device)
        if hashlib.sha256(payload.encode("utf-8")).hexdigest()
        == authority.payload_sha256
    ]
    if len(matches) != 1:
        device.capture("career-skill-specialization-authority-payload-ambiguous")
        raise RuntimeError(
            "Expected one exact specialization payload bound to workspace authority, "
            f"got {len(matches)}"
        )
    root = ET.fromstring(matches[0])
    if root.findtext("alias") != "CareerSkillSpecializationE2E":
        raise RuntimeError("The authority digest selected a different runner payload")
    return root


def target_skill(root: ET.Element) -> ET.Element:
    matches = [
        skill
        for skill in root.findall("./newskills/skills/skill")
        if skill.findtext("guid") == SKILL_ID
        and skill.findtext("suid") == SOURCE_SKILL_ID
        and skill.findtext("isknowledge") == "False"
    ]
    if len(matches) != 1:
        raise RuntimeError(f"Expected one exact typed active-skill identity, got {len(matches)}")
    return matches[0]


def assert_custom_knowledge_unchanged(root: ET.Element) -> None:
    matches = [
        skill
        for skill in root.findall("./newskills/knoskills/skill")
        if skill.findtext("guid") == KNOWLEDGE_SKILL_ID
        and skill.findtext("suid") == NULL_SOURCE_SKILL_ID
        and skill.findtext("isknowledge") == "True"
    ]
    if len(matches) != 1:
        raise RuntimeError(
            f"Expected one custom knowledge-skill identity with null source, got {len(matches)}"
        )
    knowledge = matches[0]
    expected = {
        "name": "Zoology",
        "type": "Academic",
        "skillcategory": "Academic",
        "base": "2",
        "karma": "0",
        "isnativelanguage": "False",
        "notes": "custom-knowledge-must-survive",
    }
    for name, value in expected.items():
        if knowledge.findtext(name) != value:
            raise RuntimeError(
                f"Unrelated custom knowledge <{name}> changed: {knowledge.findtext(name)!r}"
            )
    if knowledge.find("specs") is not None:
        raise RuntimeError("Active-skill specialization mutated the custom knowledge skill")


def assert_before(root: ET.Element) -> None:
    skill = target_skill(root)
    assert_custom_knowledge_unchanged(root)
    if root.findtext("karma") != "20":
        raise RuntimeError("Imported Karma balance does not match specialization authority")
    if skill.find("specs") is not None:
        raise RuntimeError("Imported specialization fixture already has a specialization")
    if skill.findtext("base") != "2" or skill.findtext("karma") != "1":
        raise RuntimeError("Imported exact parent-skill rating changed")
    if skill.findtext("notes") != "target-skill-must-survive":
        raise RuntimeError("Imported parent-skill unrelated field changed")
    if root.findtext("./customstate/sentinel") != "keep-nested-structure":
        raise RuntimeError("Imported nested authority sentinel is missing")


def assert_after(root: ET.Element) -> tuple[str, str]:
    skill = target_skill(root)
    assert_custom_knowledge_unchanged(root)
    if root.findtext("karma") != "13":
        raise RuntimeError("Exact 7-Karma specialization cost was not persisted")
    if skill.findtext("base") != "2" or skill.findtext("karma") != "1":
        raise RuntimeError("Specialization purchase changed the parent-skill rating")
    if skill.findtext("notes") != "target-skill-must-survive":
        raise RuntimeError("Specialization purchase changed unrelated skill XML")
    if root.findtext("nuyen") != "1000":
        raise RuntimeError("Specialization purchase changed Nuyen")
    sentinel = root.find("./customstate/sentinel")
    if (
        sentinel is None
        or sentinel.get("guid") != "nested-sentinel"
        or sentinel.text != "keep-nested-structure"
    ):
        raise RuntimeError("Specialization purchase changed unrelated nested XML")

    specs = skill.findall("./specs/spec")
    if len(specs) != 1:
        raise RuntimeError(f"Expected one exact saved specialization, got {len(specs)}")
    spec = specs[0]
    specialization_id = spec.findtext("guid") or ""
    uuid.UUID(specialization_id)
    expected_spec = {
        "name": EXPECTED_SPECIALIZATION,
        "free": "False",
        "expertise": "False",
    }
    for name, value in expected_spec.items():
        if spec.findtext(name) != value:
            raise RuntimeError(f"Specialization <{name}> was not exact: {spec.findtext(name)!r}")

    added = [
        expense
        for expense in root.findall("./expenses/expense")
        if expense.findtext("guid") != ORIGINAL_EXPENSE_ID
    ]
    if len(added) != 1:
        raise RuntimeError(f"Expected one generated Karma expense, got {len(added)}")
    expense = added[0]
    expense_id = expense.findtext("guid") or ""
    uuid.UUID(expense_id)
    expected_expense = {
        "amount": "-7",
        "reason": "Learned Specialization Pilot Ground Craft (Bike)",
        "type": "Karma",
        "refund": "False",
        "forcecareervisible": "False",
    }
    for name, value in expected_expense.items():
        if expense.findtext(name) != value:
            raise RuntimeError(f"Expense <{name}> was not exact: {expense.findtext(name)!r}")
    undo = expense.find("undo")
    if undo is None:
        raise RuntimeError("Specialization expense has no undo payload")
    expected_undo = {
        "karmatype": "AddSpecialization",
        "nuyentype": "AddCyberware",
        "objectid": specialization_id,
        "qty": "0",
        "extra": "",
    }
    for name, value in expected_undo.items():
        if (undo.findtext(name) or "") != value:
            raise RuntimeError(f"Undo <{name}> was not exact: {undo.findtext(name)!r}")
    return specialization_id, expense_id


def read_saved_authority(device: shared.Device) -> shared.WorkspaceAuthority:
    device.tap("Home")
    device.wait("Continue building", timeout=120)
    authority = shared.read_workspace_authority(device)
    shared.require_saved_authority(authority)
    return authority


def assert_ui_readback(device: shared.Device) -> None:
    identity = device.wait("career-skill-specialization-identity", timeout=45, scroll=True)
    rating = device.wait("career-skill-specialization-rating", timeout=45, scroll=True)
    origin = device.wait(
        "career-skill-specialization-selection-origin", timeout=45, scroll=True
    )
    identity_text = identity.attributes.get("text") or ""
    rating_text = rating.attributes.get("text") or ""
    origin_text = origin.attributes.get("text") or ""
    if "Active" not in identity_text or SKILL_ID not in identity_text:
        raise RuntimeError("Typed active parent identity was not read back")
    if "Rating 3" not in rating_text or "existing specializations 1" not in rating_text:
        raise RuntimeError("Saved specialization count was not read back after reopen")
    if "Origin SourceCatalog" not in origin_text or "Bike" not in origin_text:
        raise RuntimeError("Exact source-catalog selection origin was not read back")


def return_home_from_page(device: shared.Device) -> None:
    device.back()
    device.wait("build-career-skill-specialization", timeout=90, scroll=True, max_scrolls=45)
    device.tap("Home")
    device.wait("Continue building", timeout=120)


def prove_purchase(
    device: shared.Device,
    fixture: Path,
    fixture_sha256: str,
) -> dict[str, object]:
    device.shell("pm", "clear", shared.PACKAGE)
    initial_launch, imported = prepare_runner(device, fixture.name, fixture_sha256)
    assert_before(root_for_authority(device, imported))
    open_page(device)
    device.wait("career-skill-specialization-identity", timeout=30)
    device.wait("career-skill-specialization-selection-origin", timeout=30)
    device.tap("career-skill-specialization-review", timeout=60)
    device.tap("Cancel", timeout=60)
    device.wait("career-skill-specialization-page", timeout=30)
    device.tap("career-skill-specialization-review", timeout=60)
    device.tap("Buy specialization", timeout=60)
    device.wait("build-career-skill-specialization", timeout=180, scroll=True, max_scrolls=45)
    saved = read_saved_authority(device)
    if saved.workspace_id != imported.workspace_id:
        raise RuntimeError("Specialization save changed workspace identity")
    if saved.content_revision != imported.content_revision + 1:
        raise RuntimeError("Specialization save did not apply exactly one content revision")
    if saved.payload_sha256 == imported.payload_sha256:
        raise RuntimeError("Specialization save did not change the authority payload digest")
    specialization_id, expense_id = assert_after(root_for_authority(device, saved))

    open_page(device)
    assert_ui_readback(device)
    device.capture("career-skill-specialization-same-session-reopen")
    return_home_from_page(device)

    first_restart = shared.force_stop_and_launch_new_process(device, initial_launch)
    device.wait("Continue building", timeout=120)
    first_restored = shared.read_workspace_authority(device)
    shared.require_restored_authority(saved, first_restored)
    assert_after(root_for_authority(device, first_restored))
    open_page(device)
    assert_ui_readback(device)
    device.capture("career-skill-specialization-first-process-restart")
    return_home_from_page(device)

    second_restart = shared.force_stop_and_launch_new_process(device, first_restart.restarted)
    device.wait("Continue building", timeout=120)
    second_restored = shared.read_workspace_authority(device)
    shared.require_restored_authority(saved, second_restored)
    assert_after(root_for_authority(device, second_restored))
    open_page(device)
    assert_ui_readback(device)
    device.capture("career-skill-specialization-second-process-restart")
    return {
        "import": shared.workspace_authority_json(imported),
        "saved": shared.workspace_authority_json(saved),
        "firstRestored": shared.workspace_authority_json(first_restored),
        "secondRestored": shared.workspace_authority_json(second_restored),
        "generatedSpecializationGuid": specialization_id,
        "generatedExpenseGuid": expense_id,
        "restartProcessIds": [
            list(first_restart.restarted.process_ids),
            list(second_restart.restarted.process_ids),
        ],
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    parser.add_argument(
        "--career-runner",
        type=Path,
        default=Path(__file__).resolve().parent
        / "fixtures/career-skill-specialization-e2e.chum5",
    )
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "careerSkillSpecializationPageSha256": android_root
        / "src/Chummer.Android/Native/CareerSkillSpecializationPage.cs",
        "buildPageSha256": android_root / "src/Chummer.Android/Native/BuildPage.cs",
        "coordinatorSha256": android_root
        / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "careerSkillSpecializationRequestSha256": workspace_root
        / "chummer-presentation/Chummer.Presentation/Overview/CareerSkillSpecializationEditRequest.cs",
        "careerSkillSpecializationMutationSha256": workspace_root
        / "chummer-presentation/Chummer.Presentation/Overview/CareerSkillSpecializationMutation.cs",
        "mutationCatalogSha256": workspace_root
        / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root
        / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": workspace_root
        / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
        "careerSkillSpecializationRulesSha256": workspace_root
        / "chummer-core-engine/Chummer.Contracts/Characters/CharacterCareerSkillSpecializationRules.cs",
        "careerSkillSpecializationSourceResolverSha256": workspace_root
        / "chummer-core-engine/Chummer.Infrastructure/Xml/FileSystemCharacterSourceDataResolver.cs",
        "workspaceStoreSha256": workspace_root
        / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Career specialization source graph is incomplete: {missing!r}")

    fixture = args.career_runner.resolve()
    fixture_sha256 = shared.sha256(fixture)
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Career specialization E2E requires API 36, got {api!r}")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    if abi != "x86_64":
        raise RuntimeError(
            f"Career specialization E2E requires the hosted x86_64 phone lane, got {abi!r}"
        )
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
    verified_remote_fixture_sha256 = device.push_verified(
        fixture,
        f"/sdcard/Download/{fixture.name}",
        fixture_sha256,
    )
    journey = prove_purchase(device, fixture, fixture_sha256)
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
        "journey": "career-skill-specialization",
        "apiLevel": int(api),
        "abi": abi,
        "package": shared.PACKAGE,
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "careerFixtureSha256": fixture_sha256,
        "verifiedRemoteCareerFixtureSha256": verified_remote_fixture_sha256,
        "controlCount": len(controls),
        "controls": controls,
        "inventoryContract": {
            "rowIds": list(INVENTORY_ROW_IDS),
            "phoneStatusBefore": INVENTORY_PHONE_STATUS_BEFORE,
            "phoneStatusAfter": INVENTORY_PHONE_STATUS_AFTER,
            "phoneStatusCountDelta": INVENTORY_PHONE_STATUS_COUNT_DELTA,
            "tabletStatusDelta": {},
            "familyCountDelta": {},
            "rowCountDelta": 0,
            "completionProvenCountDelta": 0,
        },
        "authorityProofStages": journey,
        "journeys": {
            "cancelThenConfirmPurchase": "pass",
            "typedIdentityAndSourceSelection": "pass",
            "exactSpecializationExpenseUndo": "pass",
            "sameSessionReopen": "pass",
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
        print(f"Career specialization E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
