#!/usr/bin/env python3
"""Prove one exact Career Weapon short burst on an API 36 phone."""

from __future__ import annotations

import argparse
import copy
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared


CONTROL = "CharacterCareer.cmsAmmoShortBurst"
WEAPON_ID = "f1111111-1111-4111-8111-111111111111"
AMMO_GEAR_ID = "f2222222-2222-4222-8222-222222222222"
UNRELATED_WEAPON_ID = "f3333333-3333-4333-8333-333333333333"
UNRELATED_GEAR_ID = "f4444444-4444-4444-8444-444444444444"
AMMO_SLOT = 1
MODE = "short-burst"
ROUNDS_CONSUMED = 3
INITIAL_AMMO = 11
EXPECTED_AMMO = INITIAL_AMMO - ROUNDS_CONSUMED
CANONICAL_IMPORT_FIELDS = {
    "name": "CareerWeaponFireE2E",
    "alias": "CareerWeaponFireE2E",
    "metatype": "Human",
    "buildmethod": "Priority",
    "createdversion": "5.225.0",
    "appversion": "5.225.0",
    "karma": "19",
    "nuyen": "8765",
    "created": "True",
    "gameedition": "SR5",
    "settings": "223a11ff-80e0-428b-89a9-6ef1c243b8b6",
}
CONTROL_PROOF_KEYS = (
    "sourceDigestBound",
    "exactWeaponClipAmmoIdentity",
    "exactShortBurstRoundDelta",
    "expectedRevisionAtomicSave",
    "payloadDigestChanged",
    "documentDigestChanged",
    "unrelatedXmlPreserved",
    "sameSessionReopened",
    "newPidAfterForceStop",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)


def require_canonical_import_fixture(root: ET.Element) -> None:
    if root.tag != "character":
        raise RuntimeError(f"Career Weapon-Fire fixture root was {root.tag!r}, not 'character'")
    for field, expected in CANONICAL_IMPORT_FIELDS.items():
        actual = root.findtext(field)
        if actual != expected:
            raise RuntimeError(
                "Career Weapon-Fire fixture is not accepted by the canonical SR5 loader: "
                f"<{field}> expected {expected!r}, got {actual!r}"
            )


def _unique_by_guid(root: ET.Element, path: str, identity: str, label: str) -> ET.Element:
    matches = [
        node for node in root.findall(path)
        if (node.findtext("guid") or "").lower() == identity
    ]
    if len(matches) != 1:
        raise RuntimeError(f"Expected one exact {label} identity {identity}, got {len(matches)}")
    return matches[0]


def target_weapon(root: ET.Element) -> ET.Element:
    direct = _unique_by_guid(root, "./weapons/weapon", WEAPON_ID, "root Weapon")
    global_matches = [
        node for node in root.iter("weapon")
        if (node.findtext("guid") or "").lower() == WEAPON_ID
    ]
    if len(global_matches) != 1 or global_matches[0] is not direct:
        raise RuntimeError("Target Weapon Guid is duplicated or does not belong to the root Weapon owner")
    return direct


def linked_ammo(root: ET.Element) -> ET.Element:
    return _unique_by_guid(root, "./gears//gear", AMMO_GEAR_ID, "linked ammo Gear")


def active_clip(root: ET.Element) -> ET.Element:
    weapon = target_weapon(root)
    if weapon.findtext("activeammoslot") != str(AMMO_SLOT):
        raise RuntimeError("Target Weapon active ammo slot identity changed")
    containers = weapon.findall("clips")
    if len(containers) != 1:
        raise RuntimeError(f"Expected one exact clips container, got {len(containers)}")
    clips = containers[0].findall("clip")
    if len(clips) != AMMO_SLOT:
        raise RuntimeError(
            f"Expected active clip slot {AMMO_SLOT} to be the only saved clip, got {len(clips)}"
        )
    clip = clips[AMMO_SLOT - 1]
    if (clip.findtext("id") or "").lower() != AMMO_GEAR_ID:
        raise RuntimeError("Active clip is no longer linked to the exact ammo Gear identity")
    return clip


def element_sha256(element: ET.Element, omitted_children: tuple[str, ...] = ()) -> str:
    clone = copy.deepcopy(element)
    for name in omitted_children:
        for child in list(clone.findall(name)):
            clone.remove(child)
    return hashlib.sha256(ET.tostring(clone, encoding="utf-8")).hexdigest()


def unrelated_xml_authority(root: ET.Element) -> dict[str, str]:
    weapon = target_weapon(root)
    ammo = linked_ammo(root)
    unrelated_weapon = _unique_by_guid(
        root, "./weapons/weapon", UNRELATED_WEAPON_ID, "unrelated Weapon"
    )
    unrelated_gear = _unique_by_guid(
        root, "./gears//gear", UNRELATED_GEAR_ID, "unrelated Gear"
    )
    sentinel = root.find("customstate")
    if sentinel is None:
        raise RuntimeError("Unrelated root XML sentinel is missing")
    return {
        "targetWeaponExceptClipsSha256": element_sha256(weapon, ("clips",)),
        "linkedAmmoExceptQuantitySha256": element_sha256(ammo, ("qty",)),
        "unrelatedWeaponSha256": element_sha256(unrelated_weapon),
        "unrelatedGearSha256": element_sha256(unrelated_gear),
        "customStateSha256": element_sha256(sentinel),
        "karma": root.findtext("karma") or "",
        "nuyen": root.findtext("nuyen") or "",
    }


def _read_int(element: ET.Element, field: str, label: str) -> int:
    try:
        return int(element.findtext(field) or "")
    except ValueError as error:
        raise RuntimeError(f"{label} <{field}> is not an exact saved integer") from error


def assert_before(root: ET.Element) -> dict[str, str]:
    weapon = target_weapon(root)
    clip = active_clip(root)
    ammo = linked_ammo(root)
    if weapon.findtext("mode") != "SA/BF/FA" or weapon.findtext("shortburst") != "3":
        raise RuntimeError("Fixture does not bind the exact saved Short Burst mode and round cost")
    if _read_int(clip, "count", "active clip") != INITIAL_AMMO:
        raise RuntimeError("Fixture active clip count differs from the exact initial authority")
    if _read_int(ammo, "qty", "linked ammo Gear") != INITIAL_AMMO:
        raise RuntimeError("Fixture linked ammo quantity differs from the exact initial authority")
    return unrelated_xml_authority(root)


def assert_after(root: ET.Element, preserved: dict[str, str]) -> None:
    clip = active_clip(root)
    ammo = linked_ammo(root)
    if _read_int(clip, "count", "active clip") != EXPECTED_AMMO:
        raise RuntimeError("Short Burst did not persist the exact three-round active-clip delta")
    if _read_int(ammo, "qty", "linked ammo Gear") != EXPECTED_AMMO:
        raise RuntimeError("Short Burst did not persist the exact linked-ammo Gear delta")
    if unrelated_xml_authority(root) != preserved:
        raise RuntimeError("Career Weapon firing changed XML outside the exact clip/ammo quantities")


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
        payload for payload in workspace_payloads(device)
        if hashlib.sha256(payload.encode("utf-8")).hexdigest() == authority.payload_sha256
    ]
    if len(matches) != 1:
        device.capture("career-weapon-fire-authority-payload-ambiguous")
        raise RuntimeError(
            "Expected one exact Weapon-Fire payload bound to workspace authority, "
            f"got {len(matches)}"
        )
    root = ET.fromstring(matches[0])
    if root.findtext("alias") != "CareerWeaponFireE2E":
        raise RuntimeError("The authority digest selected a different runner payload")
    return root


def prepare_runner(
    device: shared.Device,
    fixture_name: str,
    fixture_sha256: str,
) -> tuple[shared.LaunchState, shared.WorkspaceAuthority]:
    launch = shared.launch_app(device)
    device.wait("Your runners", timeout=120)
    device.tap("home-open-file")
    shared.select_android_document(device, fixture_name)
    device.wait("Continue building", timeout=120)
    authority = shared.read_workspace_authority(device)
    shared.require_import_authority(authority, fixture_sha256)
    return launch, authority


def tap_exact_build_route(
    device: shared.Device,
    selector: str,
    *,
    evidence_prefix: str,
    surface_name: str,
) -> None:
    """Recover a preserved Build viewport and tap one unambiguous exact route."""
    shared.reset_scroll_to_top(device, swipes=48)
    node = device.wait_for_single_exact_resource_id(
        selector,
        timeout=120,
        scroll=True,
        max_scrolls=24,
        scroll_distance_ratio=0.22,
        evidence_prefix=evidence_prefix,
        surface_name=surface_name,
    )
    if not device.node_has_tappable_bounds(node):
        device.capture(f"{evidence_prefix}-untappable")
        raise RuntimeError(f"The exact {surface_name.lower()} is not tappable")
    x, y = node.center
    device.shell("input", "tap", str(x), str(y))


def open_page(device: shared.Device) -> None:
    shared.open_build(device, "phone")
    tap_exact_build_route(
        device,
        "build-section-tab-gear",
        evidence_prefix="career-weapon-fire-gear-section-route",
        surface_name="Build Gear section route accessibility node",
    )
    device.wait_for_single_exact_resource_id(
        f"collection-item-gear-{AMMO_GEAR_ID}",
        timeout=120,
        evidence_prefix="career-weapon-fire-gear-section-entered",
        surface_name="Exact fixture-linked Gear collection transition surface",
    )
    tap_exact_build_route(
        device,
        "build-action-tab-gear-weapons",
        evidence_prefix="career-weapon-fire-weapons-route",
        surface_name="Gear Weapons route accessibility node",
    )
    tap_exact_build_route(
        device,
        f"collection-item-weapon-{WEAPON_ID}",
        evidence_prefix="career-weapon-fire-target-weapon-route",
        surface_name="Target Weapon collection route accessibility node",
    )
    device.wait(f"collection-editor-weapon-{WEAPON_ID}", timeout=120)
    token = WEAPON_ID.replace("-", "")
    device.tap(
        f"career-weapon-fire-open-{token}",
        scroll=True,
        timeout=120,
        max_scrolls=36,
    )
    device.wait(f"career-weapon-fire-page-{token}", timeout=60)


def assert_ui_readback(device: shared.Device, expected_ammo: int) -> None:
    token = WEAPON_ID.replace("-", "")
    ammo = device.wait(f"career-weapon-fire-ammo-{token}", timeout=45, scroll=True)
    mode = device.wait(
        f"career-weapon-fire-{MODE}-{token}", timeout=45, scroll=True, max_scrolls=20
    )
    if f"{expected_ammo} rounds in active clip {AMMO_SLOT}" not in (
        ammo.attributes.get("text") or ""
    ):
        raise RuntimeError("Career Weapon active-clip ammo was not read back exactly")
    if "Short Burst · 3 rounds" not in (mode.attributes.get("text") or ""):
        raise RuntimeError("Career Weapon exact Short Burst round cost was not read back")


def read_saved_authority(device: shared.Device) -> shared.WorkspaceAuthority:
    device.tap("Home")
    device.wait("Continue building", timeout=120)
    authority = shared.read_workspace_authority(device)
    shared.require_saved_authority(authority)
    return authority


def prove_short_burst(
    device: shared.Device,
    fixture: Path,
    fixture_sha256: str,
) -> dict[str, object]:
    device.shell("pm", "clear", shared.PACKAGE)
    initial_launch, imported = prepare_runner(device, fixture.name, fixture_sha256)
    preserved = assert_before(root_for_authority(device, imported))

    open_page(device)
    assert_ui_readback(device, INITIAL_AMMO)
    token = WEAPON_ID.replace("-", "")
    device.tap(f"career-weapon-fire-{MODE}-{token}", timeout=180, scroll=True)
    device.wait(f"career-weapon-fire-open-{token}", timeout=180, scroll=True, max_scrolls=36)
    saved = read_saved_authority(device)
    if saved.workspace_id != imported.workspace_id:
        raise RuntimeError("Weapon-Fire save changed workspace identity")
    if saved.content_revision != imported.content_revision + 1:
        raise RuntimeError("Weapon-Fire save did not apply exactly one content revision")
    if saved.payload_sha256 == imported.payload_sha256:
        raise RuntimeError("Weapon-Fire save did not change the authority payload digest")
    if saved.document_sha256 == imported.document_sha256:
        raise RuntimeError("Weapon-Fire save did not change the durable document digest")
    assert_after(root_for_authority(device, saved), preserved)

    open_page(device)
    assert_ui_readback(device, EXPECTED_AMMO)
    device.capture("career-weapon-fire-same-session-reopen")

    restart = shared.force_stop_and_launch_new_process(device, initial_launch)
    device.wait("Continue building", timeout=120)
    restored = shared.read_workspace_authority(device)
    shared.require_restored_authority(saved, restored)
    assert_after(root_for_authority(device, restored), preserved)
    open_page(device)
    assert_ui_readback(device, EXPECTED_AMMO)
    device.capture("career-weapon-fire-new-process-reopen")
    return {
        "mode": MODE,
        "roundsConsumed": ROUNDS_CONSUMED,
        "weaponId": WEAPON_ID,
        "ammoSlot": AMMO_SLOT,
        "ammoGearId": AMMO_GEAR_ID,
        "unrelatedXmlAuthority": preserved,
        "import": shared.workspace_authority_json(imported),
        "saved": shared.workspace_authority_json(saved),
        "restored": shared.workspace_authority_json(restored),
        "restartProcessIds": {
            "beforeForceStop": list(restart.before_force_stop.process_ids),
            "afterForceStop": list(restart.after_force_stop.process_ids),
            "restarted": list(restart.restarted.process_ids),
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
    parser.add_argument(
        "--career-runner",
        type=Path,
        default=Path(__file__).resolve().parent / "fixtures/career-weapon-fire-e2e.chum5",
    )
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "careerWeaponFirePageSha256": android_root
        / "src/Chummer.Android/Native/CareerWeaponFirePage.cs",
        "collectionRouteSha256": android_root
        / "src/Chummer.Android/Native/CollectionEditorPages.cs",
        "coordinatorSha256": android_root
        / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "careerWeaponFireRequestSha256": workspace_root
        / "chummer-presentation/Chummer.Presentation/Overview/CareerWeaponFireRequest.cs",
        "mutationCatalogSha256": workspace_root
        / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root
        / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": workspace_root
        / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": workspace_root
        / "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
        "weaponFireRulesSha256": workspace_root
        / "chummer-core-engine/Chummer.Contracts/Characters/CharacterWeaponFireRules.cs",
        "workspaceStoreSha256": workspace_root
        / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Career Weapon-Fire source graph is incomplete: {missing!r}")

    fixture = args.career_runner.resolve()
    require_canonical_import_fixture(ET.parse(fixture).getroot())
    fixture_sha256 = shared.sha256(fixture)
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Career Weapon-Fire E2E requires API 36, got {api!r}")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    if abi != "x86_64":
        raise RuntimeError(
            f"Career Weapon-Fire E2E requires the hosted x86_64 phone lane, got {abi!r}"
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
    journey = prove_short_burst(device, fixture, fixture_sha256)
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "career-weapon-fire",
        "apiLevel": int(api),
        "abi": abi,
        "package": shared.PACKAGE,
        "apk": str(args.apk.resolve()),
        "apkSha256": shared.sha256(args.apk.resolve()),
        "driverSha256": shared.sha256(driver),
        **{key: shared.sha256(path) for key, path in source_paths.items()},
        "careerFixtureSha256": fixture_sha256,
        "verifiedRemoteCareerFixtureSha256": verified_remote_fixture_sha256,
        "controlCount": 1,
        "controls": {
            CONTROL: {key: "pass" for key in CONTROL_PROOF_KEYS},
        },
        "authorityProofStages": journey,
        "journeys": {
            "exactShortBurst": "pass",
            "sameSessionReopen": "pass",
            "newProcessRestart": "pass",
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
        print(f"Career Weapon-Fire E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
