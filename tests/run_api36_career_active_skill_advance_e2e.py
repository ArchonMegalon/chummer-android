#!/usr/bin/env python3
"""Prove one exact atomic Career Active-Skill advancement on an API 36 phone."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROLS = ("SkillControl.btnCareerIncrease",)
SKILL_ID = "11111111-1111-1111-1111-111111111111"
SOURCE_SKILL_ID = "ae91a8a6-80e7-4f52-b9eb-21725a5528a4"
ORIGINAL_EXPENSE_ID = "22222222-2222-2222-2222-222222222222"
CANONICAL_IMPORT_FIELDS = {
    "name": "CareerActiveSkillAdvanceE2E",
    "alias": "CareerActiveSkillAdvanceE2E",
    "metatype": "Human",
    "buildmethod": "Priority",
    "createdversion": "5.225.0",
    "appversion": "5.225.0",
    "karma": "20",
    "nuyen": "1000",
    "created": "True",
    "gameedition": "SR5",
    "settings": "223a11ff-80e0-428b-89a9-6ef1c243b8b6",
}
CONTROL_PROOF_KEYS = (
    "stableSkillGuid",
    "exactSourceSkillGuid",
    "ruleDigestBound",
    "confirmationRequired",
    "karmaCostExact",
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


def require_canonical_import_fixture(root: ET.Element) -> None:
    if root.tag != "character":
        raise RuntimeError(f"Career Active-Skill fixture root was {root.tag!r}, not 'character'")
    for field, expected in CANONICAL_IMPORT_FIELDS.items():
        actual = root.findtext(field)
        if actual != expected:
            raise RuntimeError(
                "Career Active-Skill fixture is not accepted by the canonical SR5 loader: "
                f"<{field}> expected {expected!r}, got {actual!r}"
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
    device.wait("CareerActiveSkillAdvanceE2E", timeout=120)
    device.wait("Continue building", timeout=120)
    authority = shared.read_workspace_authority(device)
    shared.require_import_authority(authority, fixture_sha256)
    return launch, authority


def open_page(device: shared.Device) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=12)
    device.tap(
        "build-career-active-skill",
        scroll=True,
        timeout=120,
        max_scrolls=40,
        scroll_distance_ratio=0.18,
    )
    device.wait("career-active-skill-page", timeout=60)


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
        device.capture("career-active-skill-authority-payload-ambiguous")
        raise RuntimeError(
            "Expected one exact active-skill payload bound to workspace authority, "
            f"got {len(matches)}"
        )
    root = ET.fromstring(matches[0])
    if root.findtext("alias") != "CareerActiveSkillAdvanceE2E":
        raise RuntimeError("The authority digest selected a different runner payload")
    return root


def target_skill(root: ET.Element) -> ET.Element:
    matches = [
        skill
        for skill in root.findall("./newskills/skills/skill")
        if skill.findtext("guid") == SKILL_ID
        and skill.findtext("suid") == SOURCE_SKILL_ID
    ]
    if len(matches) != 1:
        raise RuntimeError(f"Expected one exact active-skill identity, got {len(matches)}")
    return matches[0]


def assert_before(root: ET.Element) -> None:
    skill = target_skill(root)
    if root.findtext("karma") != "20" or skill.findtext("karma") != "1":
        raise RuntimeError("Imported active-skill/Karma fixture does not match authority")
    if skill.findtext("base") != "2" or skill.findtext("notes") != "target-skill-must-survive":
        raise RuntimeError("Imported target skill lost unrelated saved fields")
    if root.findtext("./customstate/sentinel") != "keep-nested-structure":
        raise RuntimeError("Imported nested authority sentinel is missing")


def assert_after(root: ET.Element) -> str:
    skill = target_skill(root)
    if root.findtext("karma") != "12":
        raise RuntimeError("Exact 8-Karma active-skill cost was not persisted")
    if skill.findtext("karma") != "2" or skill.findtext("base") != "2":
        raise RuntimeError("Only the target saved Karma rating should increase")
    if skill.findtext("notes") != "target-skill-must-survive":
        raise RuntimeError("Active-skill advancement changed unrelated skill XML")
    if root.findtext("nuyen") != "1000":
        raise RuntimeError("Active-skill advancement changed Nuyen")
    sentinel = root.find("./customstate/sentinel")
    if (
        sentinel is None
        or sentinel.get("guid") != "nested-sentinel"
        or sentinel.text != "keep-nested-structure"
    ):
        raise RuntimeError("Active-skill advancement changed unrelated nested XML")

    added = [
        expense
        for expense in root.findall("./expenses/expense")
        if expense.findtext("guid") != ORIGINAL_EXPENSE_ID
    ]
    if len(added) != 1:
        raise RuntimeError(f"Expected one generated Karma expense, got {len(added)}")
    expense = added[0]
    expense_id = expense.findtext("guid") or ""
    import uuid

    uuid.UUID(expense_id)
    expected = {
        "amount": "-8",
        "reason": "Active Skill Pilot Ground Craft 3 -> 4",
        "type": "Karma",
        "refund": "False",
        "forcecareervisible": "False",
    }
    for name, value in expected.items():
        if expense.findtext(name) != value:
            raise RuntimeError(f"Expense <{name}> was not exact: {expense.findtext(name)!r}")
    undo = expense.find("undo")
    if undo is None:
        raise RuntimeError("Active-skill expense has no undo payload")
    undo_expected = {
        "karmatype": "ImproveSkill",
        "nuyentype": "AddCyberware",
        "objectid": SKILL_ID,
        "qty": "0",
        "extra": "",
    }
    for name, value in undo_expected.items():
        if (undo.findtext(name) or "") != value:
            raise RuntimeError(f"Undo <{name}> was not exact: {undo.findtext(name)!r}")
    return expense_id


def read_saved_authority(device: shared.Device) -> shared.WorkspaceAuthority:
    device.tap("Home")
    device.wait("Continue building", timeout=120)
    authority = shared.read_workspace_authority(device)
    shared.require_saved_authority(authority)
    return authority


def assert_ui_readback(device: shared.Device) -> None:
    rating = device.wait("career-active-skill-rating", timeout=45, scroll=True)
    cost = device.wait("career-active-skill-cost", timeout=45, scroll=True)
    if "Current rating 4" not in (rating.attributes.get("text") or ""):
        raise RuntimeError("Advanced rating was not read back after reopen")
    if "Cost 10 Karma" not in (cost.attributes.get("text") or ""):
        raise RuntimeError("Next exact advancement cost was not read back after reopen")


def synchronize_build_active_skill_route(
    device: shared.Device,
    *,
    timeout: int,
    evidence_prefix: str,
) -> None:
    """Reset the preserved Build viewport, then bind one exact route going forward."""
    shared.reset_scroll_to_top(device, swipes=48)
    node = device.wait_for_single_exact_resource_id(
        "build-career-active-skill",
        timeout=timeout,
        scroll=True,
        max_scrolls=40,
        scroll_distance_ratio=0.18,
        evidence_prefix=evidence_prefix,
        surface_name="Build Career Active Skill route accessibility node",
    )
    if not device.node_has_tappable_bounds(node):
        device.capture(f"{evidence_prefix}-untappable")
        raise RuntimeError("The exact Build Career Active Skill route is not tappable")


def return_home_from_page(device: shared.Device) -> None:
    device.back()
    synchronize_build_active_skill_route(
        device,
        timeout=90,
        evidence_prefix="career-active-skill-return-route",
    )
    device.tap("Home")
    device.wait("Continue building", timeout=120)


def prove_advancement(
    device: shared.Device,
    fixture: Path,
    fixture_sha256: str,
) -> dict[str, object]:
    device.shell("pm", "clear", shared.PACKAGE)
    initial_launch, imported = prepare_runner(device, fixture.name, fixture_sha256)
    assert_before(root_for_authority(device, imported))
    open_page(device)
    device.wait("career-active-skill-rating", timeout=30)
    device.wait("career-active-skill-cost", timeout=30)
    device.tap("career-active-skill-advance", timeout=60)
    device.tap("Cancel", timeout=60)
    device.wait("career-active-skill-page", timeout=30)
    device.tap("career-active-skill-advance", timeout=60)
    device.tap("Advance", timeout=60)
    synchronize_build_active_skill_route(
        device,
        timeout=180,
        evidence_prefix="career-active-skill-post-advance-route",
    )
    saved = read_saved_authority(device)
    if saved.workspace_id != imported.workspace_id:
        raise RuntimeError("Active-skill save changed workspace identity")
    if saved.content_revision != imported.content_revision + 1:
        raise RuntimeError("Active-skill save did not apply exactly one content revision")
    if saved.payload_sha256 == imported.payload_sha256:
        raise RuntimeError("Active-skill save did not change the authority payload digest")
    expense_id = assert_after(root_for_authority(device, saved))

    open_page(device)
    assert_ui_readback(device)
    device.capture("career-active-skill-same-session-reopen")
    return_home_from_page(device)

    first_restart = shared.force_stop_and_launch_new_process(device, initial_launch)
    device.wait("Continue building", timeout=120)
    first_restored = shared.read_workspace_authority(device)
    shared.require_restored_authority(saved, first_restored)
    assert_after(root_for_authority(device, first_restored))
    open_page(device)
    assert_ui_readback(device)
    device.capture("career-active-skill-first-process-restart")
    return_home_from_page(device)

    second_restart = shared.force_stop_and_launch_new_process(device, first_restart.restarted)
    device.wait("Continue building", timeout=120)
    second_restored = shared.read_workspace_authority(device)
    shared.require_restored_authority(saved, second_restored)
    assert_after(root_for_authority(device, second_restored))
    open_page(device)
    assert_ui_readback(device)
    device.capture("career-active-skill-second-process-restart")
    return {
        "import": shared.workspace_authority_json(imported),
        "saved": shared.workspace_authority_json(saved),
        "firstRestored": shared.workspace_authority_json(first_restored),
        "secondRestored": shared.workspace_authority_json(second_restored),
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
        / "fixtures/career-active-skill-advance-e2e.chum5",
    )
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "careerActiveSkillPageSha256": android_root
        / "src/Chummer.Android/Native/CareerActiveSkillAdvancePage.cs",
        "buildPageSha256": android_root / "src/Chummer.Android/Native/BuildPage.cs",
        "coordinatorSha256": android_root
        / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "careerActiveSkillRequestSha256": workspace_root
        / "chummer-presentation/Chummer.Presentation/Overview/CareerActiveSkillAdvanceEditRequest.cs",
        "careerActiveSkillMutationSha256": workspace_root
        / "chummer-presentation/Chummer.Presentation/Overview/CareerActiveSkillAdvanceMutation.cs",
        "mutationCatalogSha256": workspace_root
        / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root
        / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": workspace_root
        / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
        "careerActiveSkillRulesSha256": workspace_root
        / "chummer-core-engine/Chummer.Contracts/Characters/CharacterCareerActiveSkillAdvanceRules.cs",
        "activeSkillSourceResolverSha256": workspace_root
        / "chummer-core-engine/Chummer.Infrastructure/Xml/FileSystemCharacterSourceDataResolver.cs",
        "workspaceStoreSha256": workspace_root
        / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Career Active-Skill source graph is incomplete: {missing!r}")

    fixture = args.career_runner.resolve()
    require_canonical_import_fixture(ET.parse(fixture).getroot())
    fixture_sha256 = shared.sha256(fixture)
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Career Active-Skill E2E requires API 36, got {api!r}")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    if abi != "x86_64":
        raise RuntimeError(
            f"Career Active-Skill E2E requires the hosted x86_64 phone lane, got {abi!r}"
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
    journey = prove_advancement(device, fixture, fixture_sha256)
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
        "journey": "career-active-skill-advance",
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
        "authorityProofStages": journey,
        "journeys": {
            "cancelThenConfirmAdvance": "pass",
            "exactExpenseUndo": "pass",
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
        print(f"Career Active-Skill E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
