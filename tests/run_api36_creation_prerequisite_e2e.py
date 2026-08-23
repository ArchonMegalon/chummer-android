#!/usr/bin/env python3
"""API-36 phone proof for the authoritative Priority/Sum-to-Ten prerequisite.

The source remains an unexecuted contract until CI or an operator runs it against a reviewed APK.
A successfully completed invocation emits a pass receipt bound to that APK and this driver.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import run_api36_creation_wizard_foundation_e2e as foundation
import run_api36_editing_e2e as shared


CATEGORIES = ("heritage", "talent", "attributes", "skills", "resources")
CREATION_KARMA_AUTHORITY_BLOCKER = "creation-karma-authority-required"
CREATION_KARMA_FIXTURE_ALIAS = "CreationGroupMembershipE2E"
CREATION_KARMA_FIXTURE_SETTINGS = "223a11ff-80e0-428b-89a9-6ef1c243b8b6"
IMPORT_AUTHORITY_TIMEOUT_SECONDS = 240
WORKSPACE_AUTHORITY_SELECTORS = (
    "home-e2e-workspace-id",
    "home-e2e-content-revision",
    "home-e2e-saved-revision",
    "home-e2e-payload-sha256",
    "home-e2e-document-sha256",
)
SHORT_AUTHORITY_BINDING = re.compile(
    r"^Revision (?P<revision>[1-9][0-9]*) · saved (?P<saved>[0-9]+) · "
    r"snapshot (?P<snapshot>[0-9a-f]{12}) · authority (?P<authority>[0-9a-f]{12})$"
)


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def node_text(device: shared.Device, selector: str, *, scroll: bool = False) -> str:
    node = device.wait(selector, timeout=60, scroll=scroll, max_scrolls=22)
    return node.attributes.get("text") or node.attributes.get("content-desc") or ""


def validate_creation_karma_fixture(path: Path) -> dict[str, object]:
    try:
        root = ET.parse(path).getroot()
    except (OSError, ET.ParseError) as error:
        raise RuntimeError(f"Creation Karma fixture is not valid XML: {path}") from error
    if root.tag != "character":
        raise RuntimeError("Creation Karma fixture must use <character> as its root")
    expected = {
        "alias": CREATION_KARMA_FIXTURE_ALIAS,
        "created": "False",
        "gameedition": "SR5",
        "settings": CREATION_KARMA_FIXTURE_SETTINGS,
    }
    for element, value in expected.items():
        if root.findtext(element) != value:
            raise RuntimeError(
                f"Creation Karma fixture requires exact <{element}>{value}</{element}>"
            )
    build_method = root.findtext("buildmethod")
    if build_method not in {"Priority", "Sum-to-Ten"}:
        raise RuntimeError(
            "Creation Karma fixture requires a production-supported Priority or Sum-to-Ten method"
        )
    try:
        karma = int(root.findtext("karma", default=""))
    except ValueError as error:
        raise RuntimeError("Creation Karma fixture requires an integer <karma>") from error
    if karma < 0:
        raise RuntimeError("Creation Karma fixture cannot have negative Karma")
    return {
        "alias": expected["alias"],
        "buildMethod": build_method,
        "settingsProfileId": expected["settings"],
        "karma": karma,
    }


def visible_workspace_authority(
    device: shared.Device,
) -> shared.WorkspaceAuthority | None:
    nodes = device.hierarchy()
    values: dict[str, str] = {}
    for selector in WORKSPACE_AUTHORITY_SELECTORS:
        node = next(
            (
                candidate
                for candidate in nodes
                if candidate.attributes.get("resource-id", "").rsplit("/", 1)[-1]
                == selector
            ),
            None,
        )
        if node is not None:
            values[selector] = node.attributes.get("text", "").strip()
    if not values or len(values) != len(WORKSPACE_AUTHORITY_SELECTORS):
        return None
    try:
        content_revision = int(values["home-e2e-content-revision"])
        saved_revision = int(values["home-e2e-saved-revision"])
    except ValueError as error:
        raise RuntimeError("Imported workspace authority revisions are not integers") from error
    authority = shared.WorkspaceAuthority(
        values["home-e2e-workspace-id"],
        content_revision,
        saved_revision,
        values["home-e2e-payload-sha256"],
        values["home-e2e-document-sha256"],
    )
    if not authority.workspace_id or authority.content_revision <= 0 or authority.saved_revision < 0:
        raise RuntimeError("Imported workspace authority identity or revisions are invalid")
    if shared.SHA256_TEXT.fullmatch(authority.payload_sha256) is None:
        raise RuntimeError("Imported workspace authority payload SHA-256 is not canonical")
    if shared.SHA256_TEXT.fullmatch(authority.document_sha256) is None:
        raise RuntimeError("Imported workspace authority document SHA-256 is not canonical")
    return authority


def wait_imported_workspace_authority(
    device: shared.Device,
    expected_payload_sha256: str,
    previous_workspace_id: str,
    *,
    timeout: int = IMPORT_AUTHORITY_TIMEOUT_SECONDS,
) -> shared.WorkspaceAuthority:
    # A created=False Priority import hydrates exact rules before OpenLocalAsync republishes the
    # diagnostic authority. On the hosted x64 API-36 runner that transition can exceed the normal
    # 90-second UI wait, so bind completion to the authority instead of accepting an alias alone.
    shared.reset_scroll_to_top(device, swipes=12)
    deadline = time.monotonic() + timeout
    last_authority: shared.WorkspaceAuthority | None = None
    while True:
        candidate = visible_workspace_authority(device)
        if candidate is not None:
            last_authority = candidate
            if candidate.workspace_id != previous_workspace_id:
                stable = shared.read_workspace_authority(device)
                try:
                    shared.require_import_authority(
                        stable,
                        expected_payload_sha256,
                        previous_workspace_id,
                    )
                except RuntimeError:
                    device.capture("creation-karma-import-authority-mismatch")
                    raise
                if shared.SHA256_TEXT.fullmatch(stable.document_sha256) is None:
                    device.capture("creation-karma-import-document-digest-invalid")
                    raise RuntimeError("Imported Creation Karma document digest is not canonical")
                return stable
        if time.monotonic() >= deadline:
            break
        time.sleep(3)
    device.capture("creation-karma-import-authority-timeout")
    raise RuntimeError(
        "Creation Karma public import did not publish a new exact workspace authority within "
        f"{timeout} seconds: previousWorkspaceId={previous_workspace_id!r}, "
        f"lastAuthority={last_authority!r}"
    )


def require_creation_method_navigation(
    node: shared.UiNode,
    *,
    ready: bool,
) -> str:
    description = (
        node.attributes.get("content-desc")
        or node.attributes.get("text")
        or ""
    )
    clickable = node.attributes.get("clickable") == "true"
    enabled = node.attributes.get("enabled") == "true"
    if ready:
        if not clickable or not enabled or CREATION_KARMA_AUTHORITY_BLOCKER in description:
            raise RuntimeError(
                "Prepared Creation Karma fixture did not enable the method navigation row: "
                f"clickable={clickable}, enabled={enabled}, detail={description!r}"
            )
    elif enabled or CREATION_KARMA_AUTHORITY_BLOCKER not in description:
        raise RuntimeError(
            "Fresh runner did not remain fail-closed without Creation Karma authority: "
            f"clickable={clickable}, enabled={enabled}, detail={description!r}"
        )
    return description


def wait_creation_method_navigation(
    device: shared.Device,
    *,
    ready: bool,
    max_scrolls: int = 22,
) -> dict[str, object]:
    shared.reset_scroll_to_top(device, swipes=max_scrolls)
    for scroll_index in range(max_scrolls + 1):
        node = device.find("creation-stage-method")
        if node is not None:
            detail = require_creation_method_navigation(node, ready=ready)
            before_tap = {
                "detail": detail,
                "clickable": node.attributes.get("clickable") == "true",
                "enabled": node.attributes.get("enabled") == "true",
            }
            if not ready:
                # UIAutomator reports a MAUI Button with a Clicked handler as clickable even while
                # IsEnabled=false. Prove the product gate itself: a physical tap must remain on the
                # dashboard and must not open the prerequisite route.
                x, y = node.center
                device.shell("input", "tap", str(x), str(y))
                time.sleep(1.25)
                if device.find("creation-prerequisite-page") is not None:
                    device.capture("creation-method-navigation-opened-without-authority")
                    raise RuntimeError(
                        "Disabled creation method navigation opened without Creation Karma authority"
                    )
                blocked_after = device.find("creation-stage-method")
                if blocked_after is None:
                    device.capture("creation-method-navigation-row-missing-after-blocked-tap")
                    raise RuntimeError(
                        "Creation method row disappeared after its disabled no-authority tap"
                    )
                after_tap = {
                    "detail": require_creation_method_navigation(blocked_after, ready=False),
                    "clickable": blocked_after.attributes.get("clickable") == "true",
                    "enabled": blocked_after.attributes.get("enabled") == "true",
                }
                if after_tap != before_tap:
                    device.capture("creation-method-navigation-changed-after-blocked-tap")
                    raise RuntimeError(
                        "Creation method no-authority state changed after its disabled tap: "
                        f"before={before_tap!r}, after={after_tap!r}"
                    )
                device.capture("creation-method-navigation-remained-blocked")
                # A ContentPage AutomationId is pruned from UIAutomator while this ScrollView is
                # deep in its content. Reset before using the page marker as a second route proof.
                shared.reset_scroll_to_top(device, swipes=max_scrolls)
                device.wait("creation-wizard-dashboard", timeout=30)
                return {
                    **before_tap,
                    "afterTap": after_tap,
                    "tapRemainedOnDashboard": True,
                }
            return before_tap
        if scroll_index < max_scrolls:
            device.swipe_up(distance_ratio=0.22)
            time.sleep(0.75)
    device.capture("creation-method-navigation-missing")
    raise RuntimeError("Creation method navigation row is absent from the phone wizard")


def require_prerequisite_binding(value: str) -> dict[str, object]:
    match = SHORT_AUTHORITY_BINDING.fullmatch(value)
    if match is None:
        raise RuntimeError(
            "Creation prerequisite binding did not expose exact revision, snapshot, and authority "
            f"digests: {value!r}"
        )
    revision = int(match.group("revision"))
    saved = int(match.group("saved"))
    if saved > revision:
        raise RuntimeError(
            f"Creation prerequisite binding saved revision exceeds content revision: {value!r}"
        )
    return {
        "contentRevision": revision,
        "savedRevision": saved,
        "snapshotDigestPrefix": match.group("snapshot"),
        "authorityDigestPrefix": match.group("authority"),
    }


def read_source_authority_digests(device: shared.Device) -> list[str]:
    required_labels = {"Authority digest", "Profile inputs", "Priorities XML"}
    seen_labels: set[str] = set()
    digests: set[str] = set()
    seen_card = False
    shared.reset_scroll_to_top(device, swipes=22)
    for scroll_index in range(23):
        nodes = device.hierarchy()
        seen_card = seen_card or any(
            shared.Device._matches(node, "creation-prerequisite-source-authority")
            for node in nodes
        )
        for node in nodes:
            values = (
                node.attributes.get("text", ""),
                node.attributes.get("content-desc", ""),
            )
            for value in values:
                if value in required_labels:
                    seen_labels.add(value)
                digests.update(shared.SHA256_TEXT.findall(value))
        if seen_card and seen_labels == required_labels and len(digests) >= 3:
            return sorted(digests)
        if scroll_index < 22:
            device.swipe_up(distance_ratio=0.22)
            time.sleep(0.75)
    device.capture("creation-prerequisite-source-authority-incomplete")
    raise RuntimeError(
        "Creation prerequisite source authority was incomplete: "
        f"card={seen_card}, labels={sorted(seen_labels)!r}, canonicalDigests={sorted(digests)!r}"
    )


def open_prerequisite(device: shared.Device) -> None:
    shared.reset_scroll_to_top(device, swipes=22)
    device.tap_until_visible(
        "creation-stage-method",
        "creation-prerequisite-page",
        scroll=True,
        max_scrolls=22,
    )
    device.wait("creation-prerequisite-karma-budget", timeout=60, scroll=True, max_scrolls=22)
    device.wait("creation-prerequisite-method", timeout=45, scroll=True, max_scrolls=22)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument(
        "--creation-karma-runner",
        type=Path,
        default=(
            Path(__file__).resolve().parent
            / "fixtures"
            / "creation-group-membership-e2e.chum5"
        ),
    )
    args = parser.parse_args()

    driver_path = Path(__file__).resolve()
    shared_path = Path(shared.__file__).resolve()
    creation_karma_runner = args.creation_karma_runner.resolve()
    creation_karma_fixture = validate_creation_karma_fixture(creation_karma_runner)
    creation_karma_fixture_sha256 = sha256(creation_karma_runner)
    remote_fixture = "/sdcard/Download/creation-group-membership-e2e.chum5"
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence.resolve())
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Creation prerequisite E2E requires API 36, got {api!r}")

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
    verified_remote_sha256 = device.push_verified(
        creation_karma_runner,
        remote_fixture,
        creation_karma_fixture_sha256,
    )
    fixture_transport_receipt = {
        "schema": "chummer.android.fixture-transport/v1",
        "status": "pass",
        "fixtures": [
            {
                "localPath": str(creation_karma_runner),
                "remotePath": remote_fixture,
                "capturedLocalSha256": creation_karma_fixture_sha256,
                "verifiedRemoteSha256": verified_remote_sha256,
            }
        ],
    }
    (device.evidence / "fixture-transport-receipt.json").write_text(
        json.dumps(fixture_transport_receipt, indent=2) + "\n",
        encoding="utf-8",
    )
    shared.launch_app(device)
    device.wait("Your runners", timeout=90)
    device.tap_until_visible("home-new-runner", "Select Build Method")
    device.tap("dialog-action-create-character", scroll=True)
    device.wait("dialog-action-complete-new-character-workflow", timeout=45, scroll=True)
    device.tap("dialog-action-complete-new-character-workflow", scroll=True)
    device.wait("creation-wizard-dashboard", timeout=90)
    foundation.assert_creation_editor_gated(device)

    fresh_dashboard_binding = node_text(device, "creation-wizard-binding", scroll=True)
    fresh_navigation = wait_creation_method_navigation(device, ready=False)
    device.capture("fresh-runner-creation-karma-authority-blocked")
    shared.reset_scroll_to_top(device, swipes=22)

    # The fixture is provisioned through the same save + Home + Android document import path
    # available to production users. Debug authority nodes are read-only evidence, never a bypass.
    device.tap("build-save-runner", scroll=True, max_scrolls=48, scroll_distance_ratio=0.22)
    device.wait("Saved.", timeout=90, scroll=True, max_scrolls=48, scroll_distance_ratio=0.22)
    device.tap("Home")
    device.wait("home-open-file", timeout=90)
    fresh_authority = shared.read_workspace_authority(device)
    shared.require_saved_authority(fresh_authority)
    device.tap("home-open-file")
    shared.select_android_document(device, creation_karma_runner.name)
    # Let the Android document activity finish returning before the bounded authority poll starts;
    # this is a shell/activity precondition, not evidence that the selected dossier was accepted.
    device.wait("Home", timeout=45)
    imported_authority = wait_imported_workspace_authority(
        device,
        creation_karma_fixture_sha256,
        fresh_authority.workspace_id,
    )
    device.wait(CREATION_KARMA_FIXTURE_ALIAS, timeout=45)
    device.wait("Continue building", timeout=45)

    shared.open_build(device, "phone")
    device.wait("creation-wizard-dashboard", timeout=90)
    foundation.assert_creation_editor_gated(device)
    dashboard_binding = node_text(device, "creation-wizard-binding", scroll=True)
    if dashboard_binding == fresh_dashboard_binding:
        raise RuntimeError("Public fixture import did not refresh the creation wizard binding")
    ready_navigation = wait_creation_method_navigation(device, ready=True)

    open_prerequisite(device)
    prerequisite_binding = node_text(device, "creation-prerequisite-binding", scroll=True)
    prerequisite_binding_authority = require_prerequisite_binding(prerequisite_binding)
    karma = node_text(device, "creation-prerequisite-karma-budget", scroll=True)
    for label in ("Total", "Used", "Remaining"):
        if label.lower() not in karma.lower():
            raise RuntimeError(f"Global Creation Karma omitted {label!r}: {karma!r}")
    source_authority_digests = read_source_authority_digests(device)

    # Build Ghost can answer from this state, but the chat route cannot touch Core mutation APIs.
    shared.reset_scroll_to_top(device, swipes=22)
    device.tap("creation-prerequisite-rook", scroll=True, max_scrolls=22)
    device.wait("rook-local-grounded-fallback", timeout=45)
    device.set_text("rook-question", "Priority question", "Which legal rank should I consider?")
    device.tap("rook-send-question")
    device.wait("rook-message-binding-1", timeout=45, scroll=True, max_scrolls=22)
    device.back()
    device.wait("creation-prerequisite-binding", timeout=45, scroll=True, max_scrolls=22)
    if node_text(device, "creation-prerequisite-binding", scroll=True) != prerequisite_binding:
        raise RuntimeError("Build Ghost changed the prerequisite workspace binding")

    selected: dict[str, str] = {}
    for category in CATEGORIES:
        device.tap(
            f"creation-prerequisite-category-{category}",
            scroll=True,
            max_scrolls=22,
        )
        device.wait("creation-prerequisite-category-page", timeout=45)
        selected[category] = foundation.tap_first_enabled_prefix(
            device,
            f"creation-prerequisite-rank-{category}-",
            max_scrolls=22,
        ) or ""
        device.wait("creation-prerequisite-page", timeout=45)

    # A plain Back from a category route preserves the exact in-memory typed rank choice.
    attributes_before = node_text(
        device,
        "creation-prerequisite-category-attributes",
        scroll=True,
    )
    device.tap("creation-prerequisite-category-attributes", scroll=True, max_scrolls=22)
    device.wait("creation-prerequisite-category-page", timeout=45)
    device.back()
    attributes_after = node_text(
        device,
        "creation-prerequisite-category-attributes",
        scroll=True,
    )
    if attributes_after != attributes_before:
        raise RuntimeError("Back navigation did not restore the typed Attribute rank selection")

    attributes_gate = node_text(
        device,
        "creation-prerequisite-attributes-disabled",
        scroll=True,
    )
    if "raw" not in attributes_gate.lower() or "metatype" not in attributes_gate.lower():
        raise RuntimeError(f"Attribute prerequisite reason is not explicit: {attributes_gate!r}")

    device.tap("creation-prerequisite-prepare-preview", scroll=True, max_scrolls=22)
    device.wait("creation-prerequisite-preview-page", timeout=60)
    device.wait("creation-prerequisite-preview-karma-budget", timeout=45, scroll=True, max_scrolls=22)
    for category in CATEGORIES:
        device.wait(
            f"creation-prerequisite-preview-assignment-{category}",
            timeout=45,
            scroll=True,
            max_scrolls=22,
        )
    device.wait("creation-prerequisite-preview-attributes-disabled", scroll=True, max_scrolls=22)
    device.tap("creation-prerequisite-confirm", scroll=True, max_scrolls=22)
    device.wait("creation-prerequisite-confirm-receipt", timeout=90, scroll=True, max_scrolls=22)
    receipt_text = node_text(device, "creation-prerequisite-confirm-receipt", scroll=True)
    if "false" not in receipt_text.lower():
        raise RuntimeError("Prerequisite receipt did not prove CharacterDocumentChanged=false")
    device.capture("creation-prerequisite-confirmed")
    device.tap("creation-prerequisite-back-to-build", scroll=True, max_scrolls=22)
    device.wait("creation-wizard-dashboard", timeout=60)
    foundation.assert_creation_editor_gated(device)
    if node_text(device, "creation-wizard-binding", scroll=True) == dashboard_binding:
        raise RuntimeError("Atomic prerequisite confirmation did not refresh the wizard revision")

    # Same-process reload and a real process restart must both restore Core's persisted draft.
    open_prerequisite(device)
    device.wait("creation-prerequisite-pending-draft", timeout=60, scroll=True, max_scrolls=22)
    resumed_attributes = node_text(
        device,
        "creation-prerequisite-category-attributes",
        scroll=True,
    )
    if "rank" not in resumed_attributes.lower():
        raise RuntimeError("Confirmed prerequisite draft did not resume its Attribute rank")

    device.shell("am", "force-stop", shared.PACKAGE)
    shared.launch_app(device)
    device.wait("Your runners", timeout=90)
    shared.open_build(device, "phone")
    device.wait("creation-wizard-dashboard", timeout=90)
    foundation.assert_creation_editor_gated(device)
    open_prerequisite(device)
    device.wait("creation-prerequisite-pending-draft", timeout=60, scroll=True, max_scrolls=22)
    device.wait("creation-prerequisite-attributes-disabled", scroll=True, max_scrolls=22)
    device.capture("creation-prerequisite-process-restart")

    receipt = {
        "schema": "chummer.android.creation-prerequisite-e2e/v1",
        "status": "pass",
        "executionStatus": "pass",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "apiLevel": int(api),
        "apk": str(args.apk.resolve()),
        "apkSha256": sha256(args.apk.resolve()),
        "driverSha256": sha256(driver_path),
        "sharedDriverSha256": sha256(shared_path),
        "journeys": {
            "freshRunnerCreationKarmaAuthorityBlocked": "pass",
            "publicRulesValidFixtureImported": "pass",
            "fixturePayloadAndDocumentDigestsVerified": "pass",
            "creationMethodNavigationEnabledAfterAuthority": "pass",
            "canonicalSourceAuthorityDigestsVisible": "pass",
            "priorityOrSumToTenAuthorityLoaded": "pass",
            "globalCreationKarmaExactTotalUsedRemaining": "pass",
            "fiveOrderedTypedCategorySelections": "pass",
            "authorityProjectedRankOptionsOnly": "pass",
            "priorityMultisetOrSumTargetEnforced": "pass",
            "selectedRankAutomationIds": selected,
            "backRestoresDraftSelection": "pass",
            "previewDigestBeforeExplicitConfirmation": "pass",
            "atomicDraftReceiptVerified": "pass",
            "characterDocumentChangedFalse": "pass",
            "rawAttributeGrantVisible": "pass",
            "attributesBlockedForMetatypeAdjustment": "pass",
            "pendingDraftSameProcessResume": "pass",
            "pendingDraftProcessRestartResume": "pass",
            "buildGhostCurrentAndNonMutating": "pass",
            "advancedEditorNeverExposedWhileCreatedFalse": "pass",
        },
        "creationKarmaProvisioning": {
            "fixture": creation_karma_fixture,
            "fixtureSha256": creation_karma_fixture_sha256,
            "verifiedRemoteFixtureSha256": verified_remote_sha256,
            "freshRunnerWorkspaceAuthority": shared.workspace_authority_json(fresh_authority),
            "importedWorkspaceAuthority": shared.workspace_authority_json(imported_authority),
            "freshNavigation": fresh_navigation,
            "readyNavigation": ready_navigation,
            "prerequisiteBinding": prerequisite_binding_authority,
            "sourceAuthorityDigests": source_authority_digests,
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
        print(f"creation prerequisite e2e failed: {error}", flush=True)
        raise SystemExit(1) from error
