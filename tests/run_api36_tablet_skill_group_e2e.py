#!/usr/bin/env python3
"""Exercise native tablet Skill Group editing on an already provisioned API36 x64 proof APK.

This separate engineering journey never installs/builds an APK or launches an emulator.
It requires an empty disposable app, imports its committed fixture through DocumentsUI,
and uses the existing read-only proof observer. It does not attest physical tablet,
release, or managed owner/generation race coverage.
"""

from __future__ import annotations

import argparse
from dataclasses import dataclass
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import time
import uuid
import xml.etree.ElementTree as ET

import api36_proof_state as proof
import run_api36_editing_e2e as shared
import run_api36_sr5_career_active_skill_wizard_e2e as common


SCHEMA = "chummer.android.tablet-skill-group-x64-e2e/v1"
FIXTURE = Path(__file__).resolve().parent / "fixtures/tablet-career-skill-group-advance-e2e.chum5"
TARGET = "11111111-1111-4111-8111-111111111111"
DECOY = "22222222-2222-4222-8222-222222222222"
OLD_EXPENSE = "cccccccc-cccc-4ccc-8ccc-cccccccccccc"
CHECKPOINT_KEY = "sr5.career.skill-group.draft.v1"
OWNER_KEY = "sr5.career.owner.v1"
PREFIX = "tablet-career-skill-group-"
ROUTE = "sr5-career/advancement/skill-group/review"
LEDGER = "androidcareerskillgroupadvanceledger"
CORE_CONTRACT = "chummer.core.sr5-career-skill-group-advance/v2"
CHECKPOINT_FIELDS = {"SchemaVersion", "Version", "RouteId", "Phase", "Draft", "Receipt", "IdempotencyKey"}
RECEIPT_ORDER = (
    "TransactionId", "Identity", "GroupKarmaBefore", "GroupKarmaAfter",
    "CharacterKarmaBefore", "CharacterKarmaAfter", "GroupRatingBefore", "GroupRatingAfter",
    "CostRatingBefore", "CostRatingAfter", "EnabledMemberCount", "ExpenseId",
    "ExpenseDateLocal", "ExpenseAmount", "ExpenseReason", "ExpenseAuthorityDigest",
    "LogicalRevisionBefore", "SourceRevisionBefore", "RuleDigestBefore",
    "LogicalRevisionAfter", "SourceRevisionAfter", "RuleDigestAfter",
)


def require(condition: bool, message: str) -> None:
    if not condition:
        raise RuntimeError(message)


def strict_json(raw: str) -> dict:
    value = json.loads(raw, object_pairs_hook=proof.object_without_duplicates)
    require(isinstance(value, dict), "Expected one JSON object")
    return value


def require_empty_home(device: shared.Device, expected: proof.ProofBuildExpectation) -> None:
    """Authenticate the preinstalled proof APK before the first import/mutation."""
    raw, process_id, _ = proof._state_file_observation(device, deadline=time.monotonic() + 15, attempt=1)
    require(raw is not None and process_id is not None, "Empty Home proof observation is unavailable")
    state = proof.validate_state(raw, expected=expected, live_process_id=process_id).payload
    require(state["workspace"] is None and state["transaction"] is None
            and state["surface"]["pageAutomationId"] == "unknown-page"
            and state["surface"]["stage"] == "runners-ready"
            and state["surface"]["wizardLane"] is None
            and state["surface"]["settled"] is False, "The proof APK does not have an empty tablet Home")


def guid(value: object) -> str:
    require(isinstance(value, str) and str(uuid.UUID(value)) == value
            and uuid.UUID(value).int != 0, "Noncanonical or missing identity")
    return value


def digest(value: object) -> str:
    require(isinstance(value, str) and re.fullmatch(r"[0-9a-f]{64}", value) is not None,
            "Noncanonical or missing digest")
    return value


def xml_shape(element: ET.Element) -> tuple:
    """Compare full XML semantics, ignoring serializer indentation only."""
    def indentation(value: str | None) -> str:
        text = value or ""
        return "" if not text.strip() and ("\n" in text or "\r" in text) else text
    return (element.tag, tuple(sorted(element.attrib.items())),
            indentation(element.text) if len(element) else element.text or "",
            tuple(xml_shape(child) for child in element), indentation(element.tail))


def one(parent: ET.Element, path: str) -> ET.Element:
    values = parent.findall(path)
    require(len(values) == 1, f"Expected exactly one XML element: {path}")
    return values[0]


def exact_children(parent: ET.Element, fields: set[str], *, containers: set[str] = frozenset()) -> None:
    require(not parent.attrib and len(parent) == len(fields) and {child.tag for child in parent} == fields
            and all(not child.attrib and (child.tag in containers or len(child) == 0) for child in parent),
            f"Duplicate, unexpected, or noncanonical XML children in {parent.tag}")


def validate_checkpoint(value: dict, authority: dict, owner: str, *, phase: int) -> dict:
    require(set(value) == CHECKPOINT_FIELDS, "Checkpoint fields differ")
    require(type(value["SchemaVersion"]) is int and value["SchemaVersion"] == 3
            and type(value["Version"]) is int and value["Version"] > 0
            and type(value["Phase"]) is int and value["Phase"] == phase
            and value["RouteId"] == ROUTE, "Checkpoint schema/phase/route differs")
    draft = value["Draft"]
    require(draft["OwnerId"] == guid(owner), "Checkpoint owner changed")
    require(draft["WorkspaceId"] == {"Value": authority["workspaceId"]}
            and draft["ExpectedContentRevision"] == authority["contentRevision"],
            "Checkpoint belongs to another workspace/revision")
    binding, plan = draft["Binding"], draft["Plan"]
    quote = binding["Quote"]
    require(binding["ContractName"] == "chummer.core.sr5-career-skill-group-quote/v1"
            and binding["WorkspaceId"] == draft["WorkspaceId"]
            and binding["WorkspaceRevision"] == draft["ExpectedContentRevision"]
            and binding["Identity"] == quote["Identity"] == plan["Identity"] == {"InternalId": TARGET},
            "Typed quote/plan group binding differs")
    for key, expected in {"Name": "Firearms", "BasePoints": 0, "KarmaPoints": 1,
                          "GroupRating": 1, "CostRating": 1, "TargetGroupRating": 2,
                          "TargetCostRating": 2, "EnabledMemberCount": 3,
                          "AvailableKarma": 30, "KarmaCost": 10, "CanAdvance": True,
                          "Disabled": False, "Broken": False, "Blocker": 0}.items():
        require(type(quote[key]) is type(expected) and quote[key] == expected,
                f"Exact fixture quote differs: {key}")
    prerequisites = quote["Prerequisites"]
    require(len(prerequisites) == 8 and {p["Prerequisite"] for p in prerequisites} == set(range(8))
            and all(p["Satisfied"] is True and p["Authority"] for p in prerequisites),
            "Typed member/legality prerequisite authority incomplete")
    for key, expected in {"SavedGroupKarmaPoints": 2, "SavedCharacterKarma": 20,
                          "TargetGroupRating": 2, "TargetCostRating": 2,
                          "EnabledMemberCount": 3, "ExpenseAmount": -10,
                          "ExpenseReason": "Skill Group Firearms 1 -> 2",
                          "KarmaUndoType": "ImproveSkillGroup", "NuyenUndoType": "AddCyberware",
                          "UndoObjectId": TARGET, "UndoQuantity": 0, "UndoExtra": ""}.items():
        require(plan[key] == expected, f"Exact typed plan differs: {key}")
    require(plan["ExpenseId"] == guid(plan["TransactionId"]), "Expense/transaction identity differs")
    for field in ("LogicalRevision", "SourceRevision", "RuleDigest"):
        require(digest(quote[field]) == plan["Expected" + field], "Plan quote digest differs")
    binding_values = [binding["ContractName"], authority["workspaceId"], str(authority["contentRevision"]),
                      TARGET, quote["LogicalRevision"], quote["SourceRevision"], quote["RuleDigest"]]
    require(hashlib.sha256("\0".join(binding_values).encode()).hexdigest() == digest(binding["BindingDigest"]),
            "Typed quote binding digest differs")
    require(digest(value["IdempotencyKey"]) == draft["ActionPlan"]["IdempotencyKey"],
            "Checkpoint action idempotency binding differs")
    require(draft["ActionPlan"]["OwnerId"] == owner
            and draft["ActionPlan"]["ActionId"] == plan["TransactionId"],
            "Checkpoint action owner/identity differs")
    require((value["Receipt"] is None) == (phase != 2), "Receipt does not match checkpoint phase")
    return plan


def validate_saved_successor(before: ET.Element, after: ET.Element, checkpoint: dict,
                             saved: dict, initial: dict) -> dict:
    require(saved["workspaceId"] == initial["workspaceId"]
            and saved["contentRevision"] == saved["savedRevision"] == initial["contentRevision"] + 1
            and saved["payloadSha256"] != initial["payloadSha256"]
            and saved["documentSha256"] != initial["documentSha256"],
            "Apply did not produce one atomic saved successor")
    plan = checkpoint["Draft"]["Plan"]
    receipt = checkpoint["Receipt"]
    require(isinstance(receipt, dict) and set(receipt) == set(RECEIPT_ORDER) | {"ReceiptDigest"},
            "Saved receipt fields differ")
    expected_receipt = {"TransactionId": plan["TransactionId"], "ExpenseId": plan["ExpenseId"],
                        "Identity": {"InternalId": TARGET}, "GroupKarmaBefore": 1, "GroupKarmaAfter": 2,
                        "CharacterKarmaBefore": 30, "CharacterKarmaAfter": 20,
                        "GroupRatingBefore": 1, "GroupRatingAfter": 2,
                        "CostRatingBefore": 1, "CostRatingAfter": 2, "EnabledMemberCount": 3,
                        "ExpenseAmount": -10, "ExpenseReason": plan["ExpenseReason"],
                        "ExpenseDateLocal": plan["ExpenseDateLocal"]}
    require(all(receipt[k] == v for k, v in expected_receipt.items()), "Saved receipt differs from reviewed plan")
    quote = checkpoint["Draft"]["Binding"]["Quote"]
    for field in ("LogicalRevision", "SourceRevision", "RuleDigest"):
        require(receipt[field + "Before"] == quote[field], "Receipt lost reviewed quote authority")
        digest(receipt[field + "After"])
    digest(receipt["ExpenseAuthorityDigest"])
    values = [CORE_CONTRACT, "receipt"] + [
        TARGET if k == "Identity" else common.dotnet_roundtrip_unspecified(str(receipt[k]))
        if k == "ExpenseDateLocal" else str(receipt[k]) for k in RECEIPT_ORDER]
    encoded = "".join(f"{len(v.encode('utf-16-le')) // 2}:{v}" for v in values).encode("utf-8")
    require(hashlib.sha256(encoded).hexdigest() == digest(receipt["ReceiptDigest"]),
            "Core receipt digest differs")
    ledger = one(after, LEDGER)
    require(ledger.attrib == {"version": "1"} and len(ledger) == 1, "Saved ledger schema differs")
    entry = one(ledger, "entry")
    exact_children(entry, {"transactionid", "expectedworkspacerevision", "committedworkspacerevision",
                           "commanddigest", "bindingdigest", "appliedresultdigest", "reviewedquotejson", "receiptjson"})
    require(entry.findtext("transactionid") == plan["TransactionId"]
            and entry.findtext("expectedworkspacerevision") == str(initial["contentRevision"])
            and entry.findtext("committedworkspacerevision") == str(saved["contentRevision"])
            and entry.findtext("bindingdigest") == checkpoint["Draft"]["Binding"]["BindingDigest"]
            and strict_json(one(entry, "receiptjson").text or "") == receipt
            and strict_json(one(entry, "reviewedquotejson").text or "") == quote,
            "Atomic ledger lost exact command/quote/receipt binding")
    digest(entry.findtext("commanddigest"))
    digest(entry.findtext("appliedresultdigest"))
    expense = one(after, f"./expenses/expense[guid='{plan['ExpenseId']}']")
    expected_expense = {"guid": plan["ExpenseId"], "date": plan["ExpenseDateLocal"][:19],
                        "amount": "-10", "reason": plan["ExpenseReason"], "type": "Karma",
                        "refund": "False", "forcecareervisible": "True"}
    exact_children(expense, set(expected_expense) | {"undo"}, containers={"undo"})
    require(all(expense.findtext(k) == v for k, v in expected_expense.items()),
            "Saved expense differs from exact typed plan")
    undo = one(expense, "undo")
    exact_children(undo, {"karmatype", "nuyentype", "objectid", "qty", "extra"})
    require({child.tag: child.text or "" for child in undo} == {
        "karmatype": "ImproveSkillGroup", "nuyentype": "AddCyberware", "objectid": TARGET,
        "qty": "0", "extra": ""}, "Saved expense undo differs")
    require(one(after, "karma").text == "20"
            and one(after, f"./newskills/groups/group[id='{TARGET}']/karma").text == "2",
            "Saved group/Karma mutation differs from receipt")
    # Remove only the declared mutation and compare every other field/member/expense.
    restored = ET.fromstring(ET.tostring(after))
    restored.remove(one(restored, LEDGER))
    one(restored, "karma").text = "30"
    one(restored, f"./newskills/groups/group[id='{TARGET}']/karma").text = "1"
    one(restored, "expenses").remove(one(restored, f"./expenses/expense[guid='{plan['ExpenseId']}']"))
    require(xml_shape(restored) == xml_shape(before),
            "Skill Group apply changed unrelated members, group, budget, or XML")
    return receipt


def require_transition(previous: dict, current: dict, *, successor: bool = False,
                       restarted: bool = False) -> None:
    require(previous["build"] == current["build"], "Build changed within tablet journey")
    if restarted:
        require(previous["processId"] != current["processId"]
                and previous["processInstanceId"] != current["processInstanceId"],
                "Restart accepted a pre-restart proof process")
    else:
        require(all(previous[k] == current[k] for k in (
            "processId", "processInstanceId", "e2eAuthorityGeneration")), "Stale owner/process authority generation")
        require(current["sequence"] > previous["sequence"], "Home did not publish a fresh observation")
    before, after = previous["workspace"], current["workspace"]
    if successor:
        require(before["workspaceId"] == after["workspaceId"]
                and after["contentRevision"] == after["savedRevision"] == before["contentRevision"] + 1,
                "Saved successor workspace/revision differs")
    else:
        require(before == after, "Reopen/restart changed saved state or replayed mutation")


class TabletDevice(shared.Device):
    @staticmethod
    def _scroll_x_ratio(selector: str) -> float:
        if selector.startswith(PREFIX):
            return 0.44 if selector in (PREFIX + TARGET, PREFIX + DECOY) else 0.82
        if selector.startswith("tablet-career-skill-groups-"):
            return 0.44
        return shared.Device._scroll_x_ratio(selector)


def tablet_environment(device: shared.Device) -> dict:
    api = device.shell("getprop", "ro.build.version.sdk")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    qemu = device.shell("getprop", "ro.kernel.qemu")
    configuration = device.shell("am", "get-config")
    widths = {int(value) for value in re.findall(r"(?:^|-)sw([0-9]+)dp(?:-|$)", configuration, re.M)}
    require(api == "36" and abi == "x86_64" and qemu == "1", "Requires an API36 x64 emulator proof device")
    require(len(widths) == 1 and min(widths) >= 600, "Actual Android smallest width is not tablet-sized")
    return {"apiLevel": 36, "abi": abi, "smallestWidthDp": min(widths),
            "configuration": configuration, "deviceKind": "tablet-profile-emulator"}


def preferences(device: shared.Device) -> dict[str, str]:
    listing = device.shell("run-as", shared.PACKAGE, "find", "shared_prefs", "-type", "f", "-name", "*.xml")
    found: dict[str, str] = {}
    for path in listing.splitlines():
        require(re.fullmatch(r"shared_prefs/[A-Za-z0-9_.-]+\.xml", path) is not None,
                "Unexpected preference path")
        root = ET.fromstring(device.run("exec-out", "run-as", shared.PACKAGE, "cat", path).stdout)
        for value in root.findall("string"):
            key = value.get("name")
            if key in (CHECKPOINT_KEY, OWNER_KEY):
                require(key not in found, "Duplicate durable owner/checkpoint")
                found[key] = value.text or ""
    return found


def saved_root(device: shared.Device, state: dict) -> ET.Element:
    authority = state["workspace"]
    workspace_id = authority["workspaceId"]
    require(re.fullmatch(r"[A-Za-z0-9_-]{1,200}", workspace_id) is not None, "Unsafe workspace identity")
    # This journey is explicitly local-owner-only. Never scan another owner's
    # files or accept a continuation/backup by coincidentally matching payload.
    path = f"files/state/workspaces/{workspace_id}.json"
    arguments = ("exec-out", "run-as", shared.PACKAGE, "cat", path)
    raw = device.run(*arguments).stdout
    require(device.run(*arguments).stdout == raw, "Workspace record changed during read")
    record = strict_json(raw)
    require(record["ContentRevision"] == authority["contentRevision"]
            and record["SavedRevision"] == authority["savedRevision"], "Saved record revisions differ from proof")
    payload = record["Envelope"]["Payload"]
    require(isinstance(payload, str) and hashlib.sha256(payload.encode()).hexdigest() == authority["payloadSha256"],
            "Saved record does not bind exact proof payload")
    root = ET.fromstring(payload)
    require(root.findtext("alias") == "TabletCareerSkillGroupE2E", "Wrong runner payload")
    return root


@dataclass(frozen=True)
class Selection:
    generation: int
    group: str
    owner: str


class Journey:
    def __init__(self, device: TabletDevice, expected: proof.ProofBuildExpectation):
        self.device, self.expected = device, expected
        self.generation = 0
        self.selection: Selection | None = None
        self.state: dict | None = None
        self.issued: dict[str, str] = {}

    def tap(self, selector: str) -> None:
        node = self.device.wait_exact_resource_id_bidirectional(
            selector, backward_scrolls=12, forward_scrolls=16, require_tappable=True,
            evidence_prefix="tablet-tap", surface_name="Tablet exact control")
        self.device.shell("input", "tap", *(str(v) for v in node.center))

    def text(self, selector: str) -> str:
        return self.device.wait_exact_resource_id_bidirectional(
            selector, backward_scrolls=12, forward_scrolls=16, require_tappable=False,
            evidence_prefix="tablet-read", surface_name="Tablet exact label").attributes.get("text", "")

    def absent_actions(self, *names: str, bottom_actions: tuple[str, ...] = ("resolve", "acknowledge")) -> None:
        require(self.text(PREFIX + "id") == TARGET, "Missing exact inspector before action-absence proof")
        previous = None
        saw_bottom_control = False
        for _ in range(16):
            nodes = self.device.hierarchy()
            require(bool(nodes), "Empty hierarchy cannot prove disabled actions")
            panel = [n for n in nodes if any(shared.Device._has_exact_resource_id(n, PREFIX + suffix)
                     for suffix in ("id", "rating", "cost", "review", "apply", "resolve", "abandon", "acknowledge", "receipt"))]
            require(bool(panel), "Inspector disappeared during action-absence proof")
            for name in names:
                require(not any(shared.Device._has_exact_resource_id(n, PREFIX + name)
                                and n.attributes.get("enabled") == "true" for n in nodes),
                        f"Unexpected enabled {name}; refusing replay")
            saw_bottom_control |= any(shared.Device._has_exact_resource_id(n, PREFIX + suffix)
                                      for n in panel for suffix in bottom_actions)
            signature = tuple(tuple(sorted(n.attributes.items())) for n in panel)
            if saw_bottom_control and signature == previous:
                return
            previous = signature
            self.device.swipe_up(x_ratio=0.82, distance_ratio=0.4)
        raise RuntimeError("Inspector action-absence scan did not reach a stable bottom")

    def destination(self, title: str) -> None:
        self.generation += 1
        self.selection = None
        self.device.open_navigation_drawer()
        # MAUI renders FlyoutItem destinations as exact text; each must be unique.
        node = self.device.wait_for_single_exact_text(title)
        self.device.shell("input", "tap", *(str(v) for v in node.center))

    def observe_home(self, label: str, *, initial_save: dict | None = None) -> dict:
        self.destination("Home")
        self.device.wait_for_single_exact_resource_id("home-open-file", scroll=True)
        # HomePage has no AutomationId in this frozen graph; its existing publisher
        # identifies it as unknown-page. Exact Home UI + stage + workspace must agree.
        deadline = time.monotonic() + 90
        while True:
            state = proof.wait_for_state(self.device, expected=self.expected, page_automation_id="unknown-page",
                                         stage="runners-ready", wizard_lane=None, timeout=90, deadline=deadline).payload
            if initial_save is None or state["workspace"]["savedRevision"] == 1:
                break
            require(state["workspace"] == initial_save and time.monotonic() < deadline,
                    "Initial save changed its workspace or exceeded the observation deadline")
            time.sleep(0.2)  # Read-only observation; never repeat the Save gesture.
        authority = shared.read_workspace_authority(self.device)
        require(shared.workspace_authority_json(authority) == {
            k: state["workspace"][k] for k in shared.workspace_authority_json(authority)},
            "Rendered Home and proof-file workspace authority differ")
        self.state = state
        (self.device.evidence / f"{label}.json").write_text(json.dumps(state, indent=2) + "\n")
        self.device.capture(label)
        return state

    def open_groups(self) -> None:
        self.destination("Build")
        panes = [self.device.wait_for_single_exact_resource_id("tablet-build-" + part + "-pane")
                 for part in ("navigation", "collection", "inspector")]
        bounds = [tuple(map(int, shared.BOUNDS.fullmatch(p.attributes["bounds"]).groups())) for p in panes]
        require(all(x2 > x1 and y2 > y1 for x1, y1, x2, y2 in bounds)
                and bounds[0][2] <= bounds[1][0] and bounds[1][2] <= bounds[2][0],
                "Tablet master/detail panes are not simultaneously laid out")
        self.tap("tablet-build-tab-tab-skills")
        self.tap("tablet-build-action-tab-skills-skills")
        self.tap("tablet-career-skill-groups-load")
        self.device.wait_for_single_exact_resource_id(PREFIX + TARGET, scroll=True)
        self.device.wait_for_single_exact_resource_id(PREFIX + DECOY, scroll=True)
        require(not any(shared.Device._has_exact_resource_id(n, PREFIX + "id") for n in self.device.hierarchy()),
                "Catalog silently selected an inspector group")

    def select(self, group: str) -> Selection:
        self.generation += 1
        self.tap(PREFIX + group)
        require(self.text(PREFIX + "id") == group, "Selected master does not own detail identity")
        self.selection = Selection(self.generation, group, guid(preferences(self.device)[OWNER_KEY]))
        return self.selection

    def guard(self, selection: Selection) -> None:
        require(selection == self.selection and selection.generation == self.generation,
                "Retired inspector generation cannot dispatch")
        require(selection.owner == preferences(self.device).get(OWNER_KEY), "Stale durable owner cannot dispatch")
        current = proof.wait_for_state(self.device, expected=self.expected, page_automation_id="unknown-page",
                                       stage="runners-ready", wizard_lane=None).payload
        require(self.state is not None and all(current[k] == self.state[k] for k in (
            "processId", "processInstanceId", "e2eAuthorityGeneration", "sequence", "workspace")),
            "Inspector lost its observed process/workspace authority generation")
        require(self.text(PREFIX + "id") == selection.group, "Inspector identity changed before dispatch")

    def issue_once(self, action: str, selection: Selection, *, fence: str) -> None:
        require(fence not in self.issued, "Action already issued; mutation replay refused")
        self.guard(selection)
        self.issued[fence] = action  # Unknown ADB outcomes keep the fence set.
        self.tap(PREFIX + action)

    def gesture_receipt(self) -> dict:
        """Report harness gesture attempts, never inferred Core dispatch counts."""
        apply_fences = [fence for fence, action in self.issued.items() if action == "apply"]
        restore_fences = [fence for fence, action in self.issued.items() if action == "resolve"]
        require(apply_fences == ["apply"] and set(restore_fences) == {
            "same-process-reopen-resolve", "process-restart-resolve"},
            "Incomplete or repeated harness apply/restore gestures cannot produce a passing receipt")
        return {"applyTapCount": len(apply_fences),
                "explicitAppliedReceiptRestoreCount": len(restore_fences),
                "applyGestureRetries": max(0, len(apply_fences) - 1),
                "harnessIssuedActionFences": dict(self.issued),
                "CoreDispatchReplayAttested": False}

    def checkpoint(self, initial: dict, owner: str, phase: int) -> dict:
        values = preferences(self.device)
        require(values.get(OWNER_KEY) == owner, "Durable owner changed")
        value = strict_json(values[CHECKPOINT_KEY])
        validate_checkpoint(value, initial["workspace"], owner, phase=phase)
        return value

    def receipt(self, checkpoint: dict) -> None:
        receipt = checkpoint["Receipt"]
        require(self.text(PREFIX + "receipt") == receipt["ReceiptDigest"]
                and self.text(PREFIX + "receipt-transaction") == receipt["TransactionId"]
                and self.text(PREFIX + "receipt-revision") == str(checkpoint["Draft"]["ExpectedContentRevision"] + 1)
                and self.text(PREFIX + "receipt-karma") == "30 → 20", "Inspector receipt differs from durable receipt")
        self.absent_actions("apply")

    def run(self, initial: dict, before: ET.Element) -> dict:
        self.state = initial
        self.open_groups()
        self.select(DECOY)
        selection = self.select(TARGET)
        self.issue_once("review", selection, fence="review")
        reviewed = self.checkpoint(initial, selection.owner, 0)
        require(self.text(PREFIX + "transaction") == reviewed["Draft"]["Plan"]["TransactionId"],
                "Reviewed UI transaction differs")
        self.device.capture("tablet-skill-group-reviewed")
        unchanged = self.observe_home("tablet-reviewed-departure")
        require_transition(initial, unchanged)
        require(xml_shape(saved_root(self.device, unchanged)) == xml_shape(before), "Review mutated runner")
        self.open_groups()
        selection = self.select(TARGET)
        self.absent_actions("apply", bottom_actions=("abandon",))
        self.issue_once("review", selection, fence="resume-reviewed")
        require(self.checkpoint(initial, selection.owner, 0) == reviewed, "Reselection changed reviewed command")
        self.issue_once("apply", selection, fence="apply")
        # The native confirmation is a separate one-shot UI gesture. Never replay Apply.
        confirm = self.device.wait_for_single_exact_resource_id("button1")
        require(confirm.attributes.get("resource-id") == "android:id/button1"
                and confirm.attributes.get("enabled") == "true"
                and confirm.attributes.get("clickable") == "true"
                and confirm.attributes.get("text", "").casefold() == "apply and verify once", "Unexpected confirmation")
        require(selection.owner == preferences(self.device).get(OWNER_KEY), "Owner changed during confirmation")
        self.device.shell("input", "tap", *(str(v) for v in confirm.center))
        self.text(PREFIX + "receipt")  # Read-only wait for commit; no retry or recovery here.
        applied = self.checkpoint(initial, selection.owner, 2)
        require(applied["Draft"] == reviewed["Draft"] and applied["IdempotencyKey"] == reviewed["IdempotencyKey"]
                and applied["Version"] == reviewed["Version"] + 2, "Apply changed reviewed action or CAS progression")
        self.receipt(applied)
        self.device.capture("tablet-skill-group-applied")
        saved = self.observe_home("tablet-skill-group-saved")
        require_transition(unchanged, saved, successor=True)
        receipt = validate_saved_successor(before, saved_root(self.device, saved), applied, saved["workspace"], initial["workspace"])
        for phase in ("same-process-reopen", "process-restart"):
            if phase == "process-restart":
                restart = shared.force_stop_and_launch_new_process(self.device, shared.current_launch_state(self.device))
                current = self.observe_home("tablet-after-process-restart")
                require(str(current["processId"]) in restart.restarted.process_ids, "Restart proof is not bound to new launch")
                require_transition(saved, current, restarted=True)
            else:
                current = saved
            self.open_groups()
            selection = self.select(TARGET)
            require(self.checkpoint(initial, selection.owner, 2) == applied, "Reopen/restart changed durable action")
            self.absent_actions("apply", "review", "acknowledge")
            self.issue_once("resolve", selection, fence=phase + "-resolve")
            self.text(PREFIX + "receipt")
            self.receipt(applied)
            self.device.capture("tablet-" + phase + "-detail")
            require(self.checkpoint(initial, selection.owner, 2) == applied, "Recovery changed checkpoint")
            if phase == "process-restart":
                self.issue_once("acknowledge", selection, fence="acknowledge")
            restored = self.observe_home("tablet-" + phase + "-unchanged")
            require_transition(current, restored)
            validate_saved_successor(before, saved_root(self.device, restored), applied, restored["workspace"], initial["workspace"])
            saved = restored
        require(CHECKPOINT_KEY not in preferences(self.device), "Acknowledgment did not remove applied checkpoint")
        return {"status": "device-pass", "executionStatus": "executed", "workspace": saved["workspace"],
                "transactionId": receipt["TransactionId"], "receiptDigest": receipt["ReceiptDigest"],
                **self.gesture_receipt(),
                "checks": ["tablet-master-detail-identity", "typed-three-member-group-quote", "reviewed-reselection",
                           "atomic-saved-successor", "same-process-reopen", "new-process-reselection", "no-duplicate-saved-mutation"]}


def execute(args: argparse.Namespace) -> dict:
    require(args.allow_mutate_disposable_runner, "--allow-mutate-disposable-runner is required")
    require(common.SAFE_ADB_SERIAL.fullmatch(args.serial) is not None, "Invalid explicit ADB serial")
    require(not args.evidence.exists() and not args.receipt.exists(), "Use fresh evidence and receipt paths")
    root = Path(__file__).resolve().parents[1]
    for path, directory in ((args.evidence, True), (args.receipt, False)):
        common.validate_external_output_path(path, label="Tablet proof output", repository_roots=(root,), expect_directory=directory)
    common.validate_output_layout(receipt=args.receipt, evidence=args.evidence)
    require(args.gate_contract.resolve() == root / "eng/api36-sr5-wizard-gate-authority.json", "Use this branch's exact gate contract")
    expected = proof.expected_build(root, args.gate_contract, args.proof_build_id)
    device = TabletDevice(args.adb, args.serial, args.evidence)
    context = {"schema": SCHEMA, "profile": "tablet", "physicalDeviceProof": False,
               "releaseEvidenceEligible": False, "managedOwnerGenerationRaceProof": False,
               "CoreDispatchReplayAttested": False,
               "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
               "status": "fail", "executionStatus": "not-run"}
    try:
        device.require_transport_stability()
        context["environment"] = tablet_environment(device)
        device.require_shared_storage_readiness(deadline=time.monotonic() + 60)
        # Already provisioned means installation and app clearing belong to the caller.
        # Refuse an existing active runner before issuing any runner mutation.
        shared.launch_app(device, attempts=1)
        journey = Journey(device, expected)
        journey.destination("Home")
        device.wait_for_single_exact_resource_id("home-open-file", scroll=True)
        require(not any(shared.Device._has_exact_resource_id(n, "home-e2e-workspace-id") for n in device.hierarchy()),
                "The tablet proof requires an empty disposable app")
        require(CHECKPOINT_KEY not in preferences(device), "Existing Skill Group checkpoint cannot be replaced")
        require_empty_home(device, expected)
        fixture_digest = shared.sha256(FIXTURE)
        before = ET.parse(FIXTURE).getroot()
        remote = "/sdcard/Download/tablet-skill-group-" + uuid.uuid4().hex + ".chum5"
        device.push_verified(FIXTURE, remote, fixture_digest)
        journey.tap("home-open-file")
        shared.select_android_document(device, remote.rsplit("/", 1)[1], evidence_prefix="tablet-skill-group-import")
        device.wait_for_single_exact_resource_id("tablet-build-layout", timeout=90)
        imported = journey.observe_home("tablet-skill-group-imported")
        require(imported["workspace"]["payloadSha256"] == fixture_digest
                and imported["workspace"]["contentRevision"] == 1
                and imported["workspace"]["savedRevision"] == 0, "Import does not bind exact committed fixture")
        require(xml_shape(saved_root(device, imported)) == xml_shape(before), "Imported fixture changed")
        journey.destination("Build")
        save = device.wait_for_single_exact_accessibility_value(
            "Save runner", evidence_prefix="tablet-initial-save", surface_name="Tablet Save runner toolbar")
        require(save.attributes.get("enabled") == "true", "Initial save unavailable")
        device.shell("input", "tap", *(str(v) for v in save.center))
        # Home's existing observer follows the save; no tablet-only save notice is invented.
        initial = journey.observe_home("tablet-skill-group-initial-saved", initial_save=imported["workspace"])
        require(initial["workspace"] == {**imported["workspace"], "savedRevision": 1},
                "Initial save changed document or content revision")
        require(all(imported[k] == initial[k] for k in ("processId", "processInstanceId", "e2eAuthorityGeneration", "build"))
                and initial["sequence"] > imported["sequence"], "Initial save lost process/generation authority")
        context.update(journey.run(initial, before))
        require(shared.sha256(FIXTURE) == fixture_digest, "Fixture changed during execution")
        context["fixtureSha256"] = fixture_digest
        context["retainedFixturePath"] = remote
    except Exception as error:
        context.update(status="fail", executionStatus="attempted", error={"type": type(error).__name__, "message": str(error)})
        raise
    finally:
        common.prepare_receipt_target(args.receipt)
        common.write_receipt_atomically(args.receipt, context)
    return context


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", required=True, type=Path)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", required=True, type=Path)
    parser.add_argument("--receipt", required=True, type=Path)
    parser.add_argument("--gate-contract", required=True, type=Path)
    parser.add_argument("--proof-build-id", required=True)
    parser.add_argument("--allow-mutate-disposable-runner", action="store_true")
    try:
        print(json.dumps(execute(parser.parse_args(argv)), indent=2))
        return 0
    except Exception as error:
        print(f"Tablet Skill Group proof failed: {type(error).__name__}: {error}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
