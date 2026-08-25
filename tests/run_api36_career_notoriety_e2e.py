#!/usr/bin/env python3
"""Prove one exact Career Notoriety edit on an API 36 phone."""

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


CONTROL = "CharacterCareer.nudNotoriety"
SELECTOR = "career-reputation-notoriety"
INITIAL_NOTORIETY = 7
EXPECTED_NOTORIETY = 8
UNRELATED_GEAR_ID = "e1111111-1111-4111-8111-111111111111"
CANONICAL_IMPORT_FIELDS = {
    "name": "CareerNotorietyE2E",
    "alias": "CareerNotorietyE2E",
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
    "exactWorkspaceFieldIdentity",
    "exactNotorietyDelta",
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
        raise RuntimeError(f"Career Notoriety fixture root was {root.tag!r}, not 'character'")
    for field, expected in CANONICAL_IMPORT_FIELDS.items():
        actual = root.findtext(field)
        if actual != expected:
            raise RuntimeError(
                "Career Notoriety fixture is not accepted by the canonical SR5 loader: "
                f"<{field}> expected {expected!r}, got {actual!r}"
            )


def _unique_child(root: ET.Element, tag: str) -> ET.Element:
    matches = root.findall(tag)
    if len(matches) != 1:
        raise RuntimeError(f"Expected one exact root <{tag}> field, got {len(matches)}")
    return matches[0]


def _unique_by_guid(root: ET.Element, path: str, identity: str, label: str) -> ET.Element:
    matches = [
        node for node in root.findall(path)
        if (node.findtext("guid") or "").lower() == identity
    ]
    if len(matches) != 1:
        raise RuntimeError(f"Expected one exact {label} identity {identity}, got {len(matches)}")
    return matches[0]


def element_sha256(element: ET.Element) -> str:
    return hashlib.sha256(ET.tostring(element, encoding="utf-8")).hexdigest()


def read_notoriety(root: ET.Element) -> int:
    field = _unique_child(root, "notoriety")
    try:
        return int(field.text or "")
    except ValueError as error:
        raise RuntimeError("Career Notoriety is not an exact saved integer") from error


def unrelated_xml_authority(root: ET.Element) -> dict[str, str]:
    gear = _unique_by_guid(root, "./gears/gear", UNRELATED_GEAR_ID, "unrelated Gear")
    sentinel = _unique_child(root, "customstate")
    return {
        "streetCredSha256": element_sha256(_unique_child(root, "streetcred")),
        "publicAwarenessSha256": element_sha256(_unique_child(root, "publicawareness")),
        "astralReputationSha256": element_sha256(
            _unique_child(root, "baseastralreputation")
        ),
        "wildReputationSha256": element_sha256(_unique_child(root, "basewildreputation")),
        "burntStreetCredSha256": element_sha256(_unique_child(root, "burntstreetcred")),
        "unrelatedGearSha256": element_sha256(gear),
        "customStateSha256": element_sha256(sentinel),
        "karma": _unique_child(root, "karma").text or "",
        "nuyen": _unique_child(root, "nuyen").text or "",
    }


def assert_before(root: ET.Element) -> dict[str, str]:
    if read_notoriety(root) != INITIAL_NOTORIETY:
        raise RuntimeError("Fixture Notoriety differs from the exact initial authority")
    return unrelated_xml_authority(root)


def assert_after(root: ET.Element, preserved: dict[str, str]) -> None:
    if read_notoriety(root) != EXPECTED_NOTORIETY:
        raise RuntimeError("Notoriety did not persist the exact one-point delta")
    if unrelated_xml_authority(root) != preserved:
        raise RuntimeError("Career Notoriety changed XML outside the exact <notoriety> field")


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
        device.capture("career-notoriety-authority-payload-ambiguous")
        raise RuntimeError(
            "Expected one exact Notoriety payload bound to workspace authority, "
            f"got {len(matches)}"
        )
    root = ET.fromstring(matches[0])
    if root.findtext("alias") != "CareerNotorietyE2E":
        raise RuntimeError("The authority digest selected a different runner payload")
    return root


def open_build_root(device: shared.Device, *, max_back_steps: int = 6) -> None:
    """Open Build and unwind its preserved phone stack to one exact root toolbar."""
    shared.open_build(device, "phone")
    back_steps = 0
    for _ in range(48):
        nodes = device.hierarchy()
        roots = [
            node
            for node in nodes
            if "build-save-runner"
            in {
                node.attributes.get("resource-id", "").rsplit("/", 1)[-1],
                node.attributes.get("content-desc", ""),
            }
        ]
        if len(roots) == 1:
            return
        if len(roots) > 1:
            device.capture("career-notoriety-build-root-cardinality-invalid")
            raise RuntimeError(
                f"Build root toolbar cardinality was {len(roots)}; expected exactly one"
            )

        navigate_up = [
            node
            for node in nodes
            if node.attributes.get("content-desc", "") == "Navigate up"
        ]
        if len(navigate_up) > 1:
            device.capture("career-notoriety-build-up-cardinality-invalid")
            raise RuntimeError(
                f"Build Navigate up cardinality was {len(navigate_up)}; expected at most one"
            )
        if len(navigate_up) == 1:
            if back_steps >= max_back_steps:
                device.capture("career-notoriety-build-root-depth-invalid")
                raise RuntimeError(
                    f"Build root remained unavailable after {max_back_steps} exact back steps"
                )
            node = navigate_up[0]
            if not device.node_has_tappable_bounds(node):
                device.capture("career-notoriety-build-up-untappable")
                raise RuntimeError("The exact Build Navigate up node is not tappable")
            x, y = node.center
            device.shell("input", "tap", str(x), str(y))
            back_steps += 1
            time.sleep(1.25)
            continue
        time.sleep(0.5)

    device.capture("career-notoriety-build-root-unavailable")
    raise RuntimeError("Timed out waiting for the exact Build root toolbar")


def tap_exact(
    device: shared.Device,
    selector: str,
    *,
    evidence_prefix: str,
    surface_name: str,
    scroll: bool = False,
    max_scrolls: int = 6,
) -> None:
    node = device.wait_for_single_exact_resource_id(
        selector,
        timeout=120,
        scroll=scroll,
        max_scrolls=max_scrolls,
        scroll_distance_ratio=0.22,
        evidence_prefix=evidence_prefix,
        surface_name=surface_name,
    )
    if not device.node_has_tappable_bounds(node):
        device.capture(f"{evidence_prefix}-untappable")
        raise RuntimeError(f"The exact {surface_name.lower()} is not tappable")
    x, y = node.center
    device.shell("input", "tap", str(x), str(y))


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


def open_page(device: shared.Device) -> None:
    open_build_root(device)
    shared.reset_scroll_to_top(device, swipes=48)
    tap_exact(
        device,
        "build-career-reputation",
        evidence_prefix="career-notoriety-reputation-route",
        surface_name="Build Career Reputation route accessibility node",
        scroll=True,
        max_scrolls=30,
    )
    device.wait_for_single_exact_accessibility_value(
        "career-reputation",
        timeout=60,
        evidence_prefix="career-notoriety-page",
        surface_name="Career Reputation page accessibility node",
    )
    device.wait_for_single_exact_resource_id(
        SELECTOR,
        timeout=60,
        scroll=True,
        max_scrolls=12,
        scroll_distance_ratio=0.22,
        evidence_prefix="career-notoriety-picker",
        surface_name="Career Notoriety picker accessibility node",
    )


def exact_picker_value(device: shared.Device) -> int:
    node = device.wait_for_single_exact_resource_id(
        SELECTOR,
        timeout=60,
        scroll=True,
        max_scrolls=12,
        scroll_distance_ratio=0.22,
        evidence_prefix="career-notoriety-readback",
        surface_name="Career Notoriety picker accessibility node",
    )
    try:
        return int((node.attributes.get("text") or "").strip())
    except ValueError as error:
        device.capture("career-notoriety-readback-invalid")
        raise RuntimeError("Career Notoriety picker did not expose an exact integer") from error


def select_notoriety(device: shared.Device, value: int) -> None:
    tap_exact(
        device,
        SELECTOR,
        evidence_prefix="career-notoriety-picker-open",
        surface_name="Career Notoriety picker accessibility node",
        scroll=True,
        max_scrolls=12,
    )
    item = device.wait_for_single_exact_accessibility_value(
        str(value),
        timeout=60,
        evidence_prefix="career-notoriety-value",
        surface_name="Career Notoriety picker value",
    )
    if not device.node_has_tappable_bounds(item):
        device.capture("career-notoriety-value-untappable")
        raise RuntimeError("The exact Career Notoriety picker value is not tappable")
    x, y = item.center
    device.shell("input", "tap", str(x), str(y))
    if exact_picker_value(device) != value:
        device.capture("career-notoriety-value-not-selected")
        raise RuntimeError(f"Career Notoriety expected {value}, but selection did not apply")


def save_reputation(device: shared.Device) -> None:
    tap_exact(
        device,
        "career-reputation-save",
        evidence_prefix="career-notoriety-save",
        surface_name="Career Reputation save accessibility node",
        scroll=True,
        max_scrolls=20,
    )
    device.wait_for_single_exact_resource_id(
        "build-career-reputation",
        timeout=120,
        scroll=True,
        max_scrolls=30,
        scroll_distance_ratio=0.22,
        evidence_prefix="career-notoriety-save-return",
        surface_name="Build Career Reputation route accessibility node",
    )


def read_saved_authority(device: shared.Device) -> shared.WorkspaceAuthority:
    device.tap("Home")
    device.wait("Continue building", timeout=120)
    authority = shared.read_workspace_authority(device)
    shared.require_saved_authority(authority)
    return authority


def prove_notoriety_edit(
    device: shared.Device,
    fixture: Path,
    fixture_sha256: str,
) -> dict[str, object]:
    device.shell("pm", "clear", shared.PACKAGE)
    initial_launch, imported = prepare_runner(device, fixture.name, fixture_sha256)
    preserved = assert_before(root_for_authority(device, imported))

    open_page(device)
    if exact_picker_value(device) != INITIAL_NOTORIETY:
        raise RuntimeError("Career Notoriety UI did not read the exact initial value")
    select_notoriety(device, EXPECTED_NOTORIETY)
    save_reputation(device)
    saved = read_saved_authority(device)
    if saved.workspace_id != imported.workspace_id:
        raise RuntimeError("Career Notoriety save changed workspace identity")
    if saved.content_revision != imported.content_revision + 1:
        raise RuntimeError("Career Notoriety save did not apply exactly one content revision")
    if saved.payload_sha256 == imported.payload_sha256:
        raise RuntimeError("Career Notoriety save did not change the authority payload digest")
    if saved.document_sha256 == imported.document_sha256:
        raise RuntimeError("Career Notoriety save did not change the durable document digest")
    assert_after(root_for_authority(device, saved), preserved)

    open_page(device)
    if exact_picker_value(device) != EXPECTED_NOTORIETY:
        raise RuntimeError("Career Notoriety same-session reopen did not read back exactly")
    device.capture("career-notoriety-same-session-reopen")

    restart = shared.force_stop_and_launch_new_process(device, initial_launch)
    device.wait("Continue building", timeout=120)
    restored = shared.read_workspace_authority(device)
    shared.require_restored_authority(saved, restored)
    assert_after(root_for_authority(device, restored), preserved)
    open_page(device)
    if exact_picker_value(device) != EXPECTED_NOTORIETY:
        raise RuntimeError("Career Notoriety new-process reopen did not read back exactly")
    device.capture("career-notoriety-new-process-reopen")
    return {
        "field": "notoriety",
        "initialValue": INITIAL_NOTORIETY,
        "savedValue": EXPECTED_NOTORIETY,
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
        default=Path(__file__).resolve().parent / "fixtures/career-notoriety-e2e.chum5",
    )
    args = parser.parse_args()

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "careerReputationPageSha256": android_root
        / "src/Chummer.Android/Native/CareerReputationPage.cs",
        "buildPageSha256": android_root / "src/Chummer.Android/Native/BuildPage.cs",
        "coordinatorSha256": android_root
        / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "careerReputationRequestSha256": workspace_root
        / "chummer-presentation/Chummer.Presentation/Overview/CareerReputationEditRequest.cs",
        "mutationCatalogSha256": workspace_root
        / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": workspace_root
        / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": workspace_root
        / "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": workspace_root
        / "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
        "characterSectionModelsSha256": workspace_root
        / "chummer-core-engine/Chummer.Contracts/Characters/CharacterSectionModels.cs",
        "characterSectionServiceSha256": workspace_root
        / "chummer-core-engine/Chummer.Infrastructure/Xml/CharacterSectionService.cs",
        "workspaceStoreSha256": workspace_root
        / "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Career Notoriety source graph is incomplete: {missing!r}")

    fixture = args.career_runner.resolve()
    root = ET.parse(fixture).getroot()
    require_canonical_import_fixture(root)
    assert_before(root)
    fixture_sha256 = shared.sha256(fixture)
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Career Notoriety E2E requires API 36, got {api!r}")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    if abi != "x86_64":
        raise RuntimeError(
            f"Career Notoriety E2E requires the hosted x86_64 phone lane, got {abi!r}"
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
    journey = prove_notoriety_edit(device, fixture, fixture_sha256)
    receipt = {
        "schema": "chummer.android.editing-e2e/v1",
        "status": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "career-notoriety",
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
            "exactNotorietyEdit": "pass",
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
        print(f"Career Notoriety E2E failed: {error}", file=sys.stderr)
        raise SystemExit(1)
