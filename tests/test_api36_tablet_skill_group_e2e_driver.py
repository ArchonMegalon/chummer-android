"""Lightweight adversarial tests; these are not Android device execution."""

from copy import deepcopy
import hashlib
import json
from pathlib import Path
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch
import xml.etree.ElementTree as ET

sys.path.insert(0, str(Path(__file__).resolve().parent))
import run_api36_tablet_skill_group_e2e as driver


OWNER = "dddddddd-dddd-4ddd-8ddd-dddddddddddd"
ACTION = "eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee"
DATE = "2081-05-02T10:20:30"
INITIAL = {"workspaceId": "runner-tablet", "contentRevision": 1, "savedRevision": 1,
           "payloadSha256": "a" * 64, "documentSha256": "b" * 64, "snapshotDigest": None}
SAVED = {**INITIAL, "contentRevision": 2, "savedRevision": 2,
         "payloadSha256": "c" * 64, "documentSha256": "d" * 64}


def state(workspace=None, *, sequence=1):
    return {"workspace": deepcopy(workspace or INITIAL), "sequence": sequence,
            "processId": 1234, "processInstanceId": OWNER, "e2eAuthorityGeneration": 1,
            "build": {"sourceCommit": "a" * 40}}


def checkpoint():
    quote = {"Identity": {"InternalId": driver.TARGET}, "Name": "Firearms", "BasePoints": 0,
             "KarmaPoints": 1, "GroupRating": 1, "CostRating": 1, "TargetGroupRating": 2,
             "TargetCostRating": 2, "EnabledMemberCount": 3, "AvailableKarma": 30,
             "KarmaCost": 10, "CanAdvance": True, "Disabled": False, "Broken": False,
             "Blocker": 0, "LogicalRevision": "1" * 64, "SourceRevision": "2" * 64,
             "RuleDigest": "3" * 64, "Prerequisites": [
                 {"Prerequisite": i, "Satisfied": True, "Authority": "Core exact projection"} for i in range(8)]}
    plan = {"Identity": quote["Identity"], "TransactionId": ACTION, "ExpenseId": ACTION,
            "ExpenseDateLocal": DATE, "SavedGroupKarmaPoints": 2, "SavedCharacterKarma": 20,
            "TargetGroupRating": 2, "TargetCostRating": 2, "EnabledMemberCount": 3,
            "ExpenseAmount": -10, "ExpenseReason": "Skill Group Firearms 1 -> 2",
            "KarmaUndoType": "ImproveSkillGroup", "NuyenUndoType": "AddCyberware",
            "UndoObjectId": driver.TARGET, "UndoQuantity": 0, "UndoExtra": "",
            **{"Expected" + field: quote[field] for field in ("LogicalRevision", "SourceRevision", "RuleDigest")}}
    binding = {"ContractName": "chummer.core.sr5-career-skill-group-quote/v1",
               "WorkspaceId": {"Value": INITIAL["workspaceId"]}, "WorkspaceRevision": 1,
               "Identity": quote["Identity"], "Quote": quote}
    binding["BindingDigest"] = hashlib.sha256("\0".join([
        binding["ContractName"], INITIAL["workspaceId"], "1", driver.TARGET,
        quote["LogicalRevision"], quote["SourceRevision"], quote["RuleDigest"]]).encode()).hexdigest()
    return {"SchemaVersion": 3, "Version": 1, "RouteId": driver.ROUTE, "Phase": 0,
            "Draft": {"OwnerId": OWNER, "WorkspaceId": binding["WorkspaceId"], "ExpectedContentRevision": 1,
                      "Binding": binding, "Plan": plan,
                      "ActionPlan": {"OwnerId": OWNER, "ActionId": ACTION, "IdempotencyKey": "4" * 64}},
            "Receipt": None, "IdempotencyKey": "4" * 64}


def applied_document():
    before = ET.parse(driver.FIXTURE).getroot()
    after = deepcopy(before)
    cp = checkpoint()
    cp.update(Phase=2, Version=3)
    receipt = dict(zip(driver.RECEIPT_ORDER, [
        ACTION, {"InternalId": driver.TARGET}, 1, 2, 30, 20, 1, 2, 1, 2, 3, ACTION, DATE,
        -10, "Skill Group Firearms 1 -> 2", "5" * 64, "1" * 64, "2" * 64, "3" * 64,
        "6" * 64, "7" * 64, "3" * 64]))
    values = [driver.CORE_CONTRACT, "receipt"] + [
        driver.TARGET if field == "Identity" else DATE + ".0000000" if field == "ExpenseDateLocal"
        else str(receipt[field]) for field in driver.RECEIPT_ORDER]
    receipt["ReceiptDigest"] = hashlib.sha256("".join(f"{len(v)}:{v}" for v in values).encode()).hexdigest()
    cp["Receipt"] = receipt
    after.find("karma").text = "20"
    after.find(f"./newskills/groups/group[id='{driver.TARGET}']/karma").text = "2"
    expense = ET.SubElement(after.find("expenses"), "expense")
    for name, value in {"guid": ACTION, "date": DATE, "amount": "-10", "reason": receipt["ExpenseReason"],
                        "type": "Karma", "refund": "False", "forcecareervisible": "True"}.items():
        ET.SubElement(expense, name).text = value
    undo = ET.SubElement(expense, "undo")
    for name, value in {"karmatype": "ImproveSkillGroup", "nuyentype": "AddCyberware", "objectid": driver.TARGET,
                        "qty": "0", "extra": ""}.items():
        ET.SubElement(undo, name).text = value
    entry = ET.SubElement(ET.SubElement(after, driver.LEDGER, version="1"), "entry")
    for name, value in {"transactionid": ACTION, "expectedworkspacerevision": "1", "committedworkspacerevision": "2",
                        "commanddigest": "8" * 64, "bindingdigest": cp["Draft"]["Binding"]["BindingDigest"],
                        "appliedresultdigest": "9" * 64, "reviewedquotejson": json.dumps(cp["Draft"]["Binding"]["Quote"]),
                        "receiptjson": json.dumps(receipt)}.items():
        ET.SubElement(entry, name).text = value
    return before, after, cp


class TabletSkillGroupValidatorTests(unittest.TestCase):
    def test_fixture_has_exact_canonical_three_member_groups_and_budget(self):
        root = ET.parse(driver.FIXTURE).getroot()
        self.assertEqual(root.findtext("created"), "True")
        self.assertEqual(root.findtext("gameedition"), "SR5")
        self.assertEqual(root.findtext("karma"), "30")
        self.assertEqual(root.findtext("nuyen"), "1000")
        self.assertEqual([(g.findtext("id"), g.findtext("name"), g.findtext("base"), g.findtext("karma"))
                          for g in root.findall("./newskills/groups/group")], [
            (driver.TARGET, "Firearms", "0", "1"), (driver.DECOY, "Electronics", "0", "1")])
        expected_sources = ["788b387b-ee41-4e6a-bf22-481a8cc4cf9f", "64088b25-de37-4d71-8800-4a430fde08af",
                            "adf31a50-b228-4e09-a09c-46ab9f5e59a1", "1c14bf0d-cc69-4126-9a95-1f2429c11aa5",
                            "41e184e0-7273-403a-9300-fa29a1707bf0", "b693f3bf-48dc-4570-9743-d94d14ee698b"]
        members = root.findall("./newskills/skills/skill")
        self.assertEqual([s.findtext("suid") for s in members], expected_sources)
        self.assertEqual(len({s.findtext("guid") for s in members}), 6)
        self.assertTrue(all(s.findtext("base") == s.findtext("karma") == "0" for s in members))

    def test_checkpoint_rejects_wrong_owner_group_revision_quote_and_phase(self):
        good = checkpoint()
        driver.validate_checkpoint(good, INITIAL, OWNER, phase=0)
        changes = [lambda c: c["Draft"].update(OwnerId=ACTION),
                   lambda c: c["Draft"].update(ExpectedContentRevision=2),
                   lambda c: c["Draft"]["Binding"].update(Identity={"InternalId": driver.DECOY}),
                   lambda c: c["Draft"]["Binding"]["Quote"].update(EnabledMemberCount=2),
                   lambda c: c["Draft"]["Binding"]["Quote"].update(KarmaCost=5),
                   lambda c: c["Draft"]["Binding"].update(BindingDigest="f" * 64),
                   lambda c: c.update(Phase=1), lambda c: c.update(Phase=False)]
        for change in changes:
            with self.subTest(change=change):
                bad = deepcopy(good)
                change(bad)
                with self.assertRaises(RuntimeError):
                    driver.validate_checkpoint(bad, INITIAL, OWNER, phase=0)

    def test_exact_atomic_successor_is_accepted(self):
        before, after, cp = applied_document()
        self.assertEqual(driver.validate_saved_successor(before, after, cp, SAVED, INITIAL), cp["Receipt"])

    def test_saved_validator_rejects_mutation_replay_and_unrelated_xml_changes(self):
        def change(path, value):
            return lambda root: setattr(root.find(path), "text", value)
        mutations = [change("karma", "10"), change("nuyen", "999"),
                     change(f"./newskills/groups/group[id='{driver.TARGET}']/karma", "3"),
                     change(f"./newskills/groups/group[id='{driver.DECOY}']/karma", "2"),
                     change("./newskills/skills/skill/karma", "1"),
                     change("./newskills/skills/skill/notes", " target-firearms-automatics-must-survive "),
                     change("./customstate/sentinel", " keep-nested-structure "),
                     lambda root: setattr(root.find("./customstate/sentinel"), "tail", "lost mixed content"),
                     lambda root: root.find("expenses").append(deepcopy(root.find("expenses/expense"))),
                     lambda root: root.find(driver.LEDGER).append(deepcopy(root.find(driver.LEDGER + "/entry")))]
        for mutation in mutations:
            with self.subTest(mutation=mutation):
                before, after, cp = applied_document()
                mutation(after)
                with self.assertRaises(RuntimeError):
                    driver.validate_saved_successor(before, after, cp, SAVED, INITIAL)

    def test_expense_undo_and_ledger_reject_duplicate_and_unknown_fields(self):
        for suffix, fields in [(f"expenses/expense[guid='{ACTION}']", ("amount", "unexpected")),
                               (f"expenses/expense[guid='{ACTION}']/undo", ("qty", "unexpected")),
                               (driver.LEDGER + "/entry", ("commanddigest", "unexpected"))]:
            for field in fields:
                with self.subTest(suffix=suffix, field=field):
                    before, after, cp = applied_document()
                    ET.SubElement(after.find(suffix), field).text = "0"
                    with self.assertRaises(RuntimeError):
                        driver.validate_saved_successor(before, after, cp, SAVED, INITIAL)

    def test_receipt_digest_corruption_or_unsaved_successor_rejected(self):
        before, after, cp = applied_document()
        cp["Receipt"]["ReceiptDigest"] = "a" * 64
        with self.assertRaisesRegex(RuntimeError, "receipt digest"):
            driver.validate_saved_successor(before, after, cp, SAVED, INITIAL)
        before, after, cp = applied_document()
        for wrong in ({**SAVED, "savedRevision": 1}, {**SAVED, "contentRevision": 3},
                      {**SAVED, "workspaceId": "other"}):
            with self.assertRaises(RuntimeError):
                driver.validate_saved_successor(before, after, cp, wrong, INITIAL)

    def test_xml_normalization_only_ignores_indentation(self):
        plain = ET.fromstring("<root><notes> text </notes><child /></root>")
        pretty = ET.fromstring("<root>\n  <notes> text </notes>\n  <child />\n</root>")
        self.assertEqual(driver.xml_shape(plain), driver.xml_shape(pretty))
        for xml in ("<root><notes>text</notes><child /></root>",
                    "<root><notes> text </notes>meaningful<child /></root>",
                    "<root><notes> text </notes> <child /></root>"):
            self.assertNotEqual(driver.xml_shape(plain), driver.xml_shape(ET.fromstring(xml)))

    def test_reopen_generation_and_new_process_authority(self):
        previous, current = state(), state(sequence=2)
        driver.require_transition(previous, current)
        for key, value in [("processId", 999), ("processInstanceId", ACTION),
                           ("e2eAuthorityGeneration", 2), ("sequence", 1)]:
            wrong = {**current, key: value}
            with self.assertRaises(RuntimeError):
                driver.require_transition(previous, wrong)
        restarted = {**current, "processId": 999, "processInstanceId": ACTION, "sequence": 1}
        driver.require_transition(previous, restarted, restarted=True)
        for wrong in (previous, {**restarted, "processId": 1234}, {**restarted, "processInstanceId": OWNER},
                      {**restarted, "workspace": SAVED}):
            with self.assertRaises(RuntimeError):
                driver.require_transition(previous, wrong, restarted=True)

    def test_actual_tablet_width_and_x64_required(self):
        for width, abi, qemu, accepted in [(800, "x86_64", "1", True), (599, "x86_64", "1", False),
                                          (800, "arm64-v8a", "0", False)]:
            device = Mock()
            device.shell.side_effect = ["36", abi, qemu, f"config: en-rUS-sw{width}dp-w1280dp-h800dp-xlarge-land"]
            if accepted:
                self.assertEqual(driver.tablet_environment(device)["smallestWidthDp"], width)
            else:
                with self.assertRaises(RuntimeError):
                    driver.tablet_environment(device)

    def test_saved_record_uses_only_exact_workspace_json_and_revisions(self):
        payload = driver.FIXTURE.read_text()
        authority = {**INITIAL, "payloadSha256": hashlib.sha256(payload.encode()).hexdigest()}
        record = {"ContentRevision": 1, "SavedRevision": 1, "Envelope": {"Payload": payload}}
        device = Mock()
        device.run.return_value = SimpleNamespace(stdout=json.dumps(record))
        driver.saved_root(device, state(authority))
        for call in device.run.call_args_list:
            self.assertEqual(call.args[-1], "files/state/workspaces/runner-tablet.json")
        device.shell.assert_not_called()  # No state scan, .lock, or owner/backup fallback.
        record["SavedRevision"] = 0
        device.run.return_value = SimpleNamespace(stdout=json.dumps(record))
        with self.assertRaises(RuntimeError):
            driver.saved_root(device, state(authority))


class TabletDispatchTests(unittest.TestCase):
    def journey(self):
        journey = driver.Journey(Mock(), Mock())
        journey.state = state()
        journey.generation = 2
        journey.selection = driver.Selection(2, driver.TARGET, OWNER)
        journey.text = Mock(return_value=driver.TARGET)
        journey.tap = Mock()
        return journey

    def test_retired_selection_or_owner_never_issues_action(self):
        journey = self.journey()
        with self.assertRaises(RuntimeError):
            journey.issue_once("apply", driver.Selection(1, driver.TARGET, OWNER), fence="apply")
        with patch.object(driver, "preferences", return_value={driver.OWNER_KEY: ACTION}):
            with self.assertRaises(RuntimeError):
                journey.issue_once("apply", journey.selection, fence="apply")
        journey.tap.assert_not_called()

    def test_unknown_tap_outcome_never_retries_apply(self):
        journey = self.journey()
        journey.tap.side_effect = TimeoutError("unknown tap outcome")
        with patch.object(driver, "preferences", return_value={driver.OWNER_KEY: OWNER}), \
             patch.object(driver.proof, "wait_for_state", return_value=SimpleNamespace(payload=journey.state)):
            with self.assertRaises(TimeoutError):
                journey.issue_once("apply", journey.selection, fence="apply")
            with self.assertRaisesRegex(RuntimeError, "replay refused"):
                journey.issue_once("apply", journey.selection, fence="apply")
        journey.tap.assert_called_once()

    def test_changed_proof_generation_or_detail_prevents_dispatch(self):
        for change in ("generation", "detail"):
            journey = self.journey()
            current = deepcopy(journey.state)
            if change == "generation":
                current["e2eAuthorityGeneration"] += 1
            else:
                journey.text.return_value = driver.DECOY
            with patch.object(driver, "preferences", return_value={driver.OWNER_KEY: OWNER}), \
                 patch.object(driver.proof, "wait_for_state", return_value=SimpleNamespace(payload=current)):
                with self.assertRaises(RuntimeError):
                    journey.issue_once("apply", journey.selection, fence="apply")
            journey.tap.assert_not_called()

    def test_absence_requires_nonempty_inspector_and_scans_lower_controls(self):
        journey = self.journey()
        journey.device.hierarchy.return_value = []
        with self.assertRaisesRegex(RuntimeError, "Empty hierarchy"):
            journey.absent_actions("apply")
        def node(name):
            return SimpleNamespace(attributes={"resource-id": driver.PREFIX + name, "enabled": "true"})
        journey.device.hierarchy.side_effect = [[node("id")], [node("apply"), node("resolve")]]
        with self.assertRaisesRegex(RuntimeError, "Unexpected enabled apply"):
            journey.absent_actions("apply")
        journey.device.swipe_up.assert_called_once()


if __name__ == "__main__":
    unittest.main()
