from __future__ import annotations

import ast
import copy
import importlib.util
from pathlib import Path
import sys
from types import SimpleNamespace
import unittest
from unittest import mock
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
DRIVER = ROOT / "tests/run_api36_sr5_before_run_edge_physical_e2e.py"
FIXTURE = ROOT / "tests/fixtures/sr5-before-run-edge-physical-e2e.chum5"
sys.path.insert(0, str(DRIVER.parent))
SPEC = importlib.util.spec_from_file_location("before_run_physical_driver", DRIVER)
assert SPEC is not None and SPEC.loader is not None
driver = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = driver
SPEC.loader.exec_module(driver)


def reviewed_transaction() -> dict[str, object]:
    contract = driver.expected_action_contract(
        driver.SPEC,
        "workspace-before-run",
        7,
    )
    identity = copy.deepcopy(contract["identity"])
    transaction: dict[str, object] = {
        "SchemaVersion": 1,
        "Version": 1,
        "Phase": 0,
        "OwnerId": "11111111-1111-1111-1111-111111111111",
        "TransactionId": "22222222-2222-2222-2222-222222222222",
        "IdempotencyKey": "",
        "Review": {
            "Schema": "chummer.sr5_table_wizard.checkpoint.v1",
            "Lane": driver.LANE_VALUE,
            "WorkspaceId": "workspace-before-run",
            "WorkspaceRevision": 7,
            "SnapshotDigest": contract["snapshotDigest"],
            "SelectedAction": identity,
        },
        "Quote": copy.deepcopy(contract["quote"]),
        "ExpectedPostconditionDigest": contract["postcondition"],
        "Receipt": None,
        "JournalDigest": "",
    }
    transaction["IdempotencyKey"] = driver.length_prefixed_hash(
        "chummer.android.sr5-table-transaction-idempotency/v1",
        transaction["OwnerId"],
        transaction["TransactionId"],
        "workspace-before-run",
        7,
        contract["snapshotDigest"],
        identity["ActionDigest"],
        contract["postcondition"],
    )
    transaction["JournalDigest"] = driver.expected_journal_digest(transaction)
    return transaction


def applied_transaction() -> dict[str, object]:
    transaction = reviewed_transaction()
    transaction["Version"] = 3
    transaction["Phase"] = 2
    quote = transaction["Quote"]
    assert isinstance(quote, dict)
    identity = quote["Identity"]
    assert isinstance(identity, dict)
    receipt: dict[str, object] = {
        "ContractName": driver.RECEIPT_CONTRACT,
        "TransactionId": transaction["TransactionId"],
        "IdempotencyKey": transaction["IdempotencyKey"],
        "WorkspaceId": "workspace-before-run",
        "ExpectedWorkspaceRevision": 7,
        "AppliedWorkspaceRevision": 8,
        "ActionId": identity["ActionId"],
        "ActionKind": driver.ACTION_KIND,
        "ActionDigest": identity["ActionDigest"],
        "ExpectedPostconditionDigest": transaction["ExpectedPostconditionDigest"],
        "ObservedPostconditionDigest": transaction["ExpectedPostconditionDigest"],
        "ReceiptDigest": "",
    }
    receipt["ReceiptDigest"] = driver.expected_receipt_digest(receipt)
    transaction["Receipt"] = receipt
    transaction["JournalDigest"] = driver.expected_journal_digest(transaction)
    return transaction


def successor_state() -> dict[str, object]:
    root = ET.parse(FIXTURE).getroot()
    before = driver.assert_before_state(root)
    saved = copy.deepcopy(root)
    saved.find("edgeused").text = "1"  # type: ignore[union-attr]
    return driver.assert_after_state(saved, before)


class BeforeRunPhysicalDriverContractTests(unittest.TestCase):
    def test_driver_is_separate_physical_api36_arm64_and_build_provenance_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        for marker in (
            "load_and_verify_manifest(",
            "physical.android_device_observation(device)",
            'device.require_transport_stability(expected_api_level="36")',
            "device.install_verified(",
            "physical.source_graph_snapshot(",
            '"profile": "phone"',
            '"releaseEvidenceStatus": "source-and-apk-bound-local-build-not-release-attested"',
            '"restartAndRecoverExactReceipt": "pass"',
            '"restartAndReopenSavedSuccessor": "pass"',
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, source)
        self.assertEqual(3, source.count("shared.force_stop_and_launch_new_process("))
        self.assertNotIn('"profile": "tablet"', source)
        self.assertNotIn("subprocess.run([str(args.adb)", source)

    def test_scope_is_one_typed_spend_edge_action_and_exclusions_are_explicit(self) -> None:
        self.assertEqual(0, driver.SPEC.action_kind)
        self.assertEqual("before-run", driver.SPEC.lane)
        self.assertIn("Spend exactly one point", driver.SPEC.representative_action)
        for excluded in (
            "loadout",
            "preparation purchases",
            "healing",
            "contacts",
            "commitments",
            "tablet",
        ):
            self.assertIn(excluded, driver.SPEC.excluded_scope)

    def test_fixture_is_exact_career_sr5_edge_zero_with_unrelated_sentinel(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        driver.require_before_run_fixture(root)
        self.assertEqual("True", root.findtext("created"))
        self.assertEqual("SR5", root.findtext("gameedition"))
        self.assertEqual("0", root.findtext("edgeused"))

        saved = ET.fromstring(ET.tostring(root, encoding="unicode"))
        saved.find("edgeused").text = "1"  # type: ignore[union-attr]
        before = driver.assert_before_state(root)
        driver.assert_after_state(saved, before)
        saved.find("./customstate/sentinel").text = "tampered"  # type: ignore[union-attr]
        with self.assertRaisesRegex(RuntimeError, "unrelated"):
            driver.assert_after_state(saved, before)

        mixed = ET.fromstring(ET.tostring(root, encoding="unicode"))
        mixed.find("edgeused").text = "1"  # type: ignore[union-attr]
        mixed.find("./attributes/attribute/totalvalue").text = "5"  # type: ignore[union-attr]
        with self.assertRaisesRegex(RuntimeError, "total Edge"):
            driver.assert_after_state(mixed, before)

        extraneous = ET.fromstring(ET.tostring(root, encoding="unicode"))
        extraneous.find("edgeused").text = "1"  # type: ignore[union-attr]
        ET.SubElement(extraneous, "pass-shaped-extra").text = "unexpected"
        with self.assertRaisesRegex(RuntimeError, "outside the exact"):
            driver.assert_after_state(extraneous, before)

    def test_review_and_applied_receipt_bind_lane_action_workspace_and_revision_plus_one(self) -> None:
        reviewed = reviewed_transaction()
        self.assertIsNone(
            driver.validate_transaction(
                reviewed,
                spec=driver.SPEC,
                workspace_id="workspace-before-run",
                expected_revision=7,
                phase=0,
                version=1,
                require_receipt=False,
            )
        )
        applied = applied_transaction()
        receipt = driver.validate_transaction(
            applied,
            spec=driver.SPEC,
            workspace_id="workspace-before-run",
            expected_revision=7,
            phase=2,
            version=3,
            require_receipt=True,
        )
        self.assertIsNotNone(receipt)
        self.assertEqual(8, receipt["AppliedWorkspaceRevision"])
        driver.require_same_review(reviewed, applied)

    def test_transaction_and_receipt_tampering_fail_closed(self) -> None:
        hostile_cases = (
            ("lane", ("Review", "Lane"), 1),
            ("workspace", ("Review", "WorkspaceId"), "other"),
            ("action", ("Quote", "Identity", "Kind"), 2),
            ("quote delta", ("Quote", "EdgeUsedAfter"), 2),
            ("bool-shaped enum", ("Quote", "Identity", "Kind"), False),
            ("arbitrary CAS", ("Version",), 999),
            ("journal digest", ("JournalDigest",), "sha256:" + "9" * 64),
            ("revision", ("Receipt", "AppliedWorkspaceRevision"), 9),
            ("receipt digest", ("Receipt", "ReceiptDigest"), "sha256:" + "0" * 64),
        )
        for label, path, value in hostile_cases:
            with self.subTest(label=label):
                hostile = applied_transaction()
                target: object = hostile
                for field in path[:-1]:
                    assert isinstance(target, dict)
                    target = target[field]
                assert isinstance(target, dict)
                target[path[-1]] = value
                with self.assertRaises(RuntimeError):
                    driver.validate_transaction(
                        hostile,
                        spec=driver.SPEC,
                        workspace_id="workspace-before-run",
                        expected_revision=7,
                        phase=2,
                        version=3,
                        require_receipt=True,
                    )

    def test_before_run_identity_quote_snapshot_postcondition_and_journal_are_computed(self) -> None:
        transaction = reviewed_transaction()
        contract = driver.expected_action_contract(
            driver.SPEC,
            "workspace-before-run",
            7,
        )
        self.assertEqual(contract["identity"], transaction["Quote"]["Identity"])
        self.assertEqual("before-run.edge.spend", transaction["Quote"]["Identity"]["ActionId"])
        self.assertEqual((0, 1), (
            transaction["Quote"]["EdgeUsedBefore"],
            transaction["Quote"]["EdgeUsedAfter"],
        ))
        self.assertEqual(contract["snapshotDigest"], transaction["Review"]["SnapshotDigest"])
        self.assertEqual(contract["postcondition"], transaction["ExpectedPostconditionDigest"])
        self.assertEqual(driver.expected_journal_digest(transaction), transaction["JournalDigest"])

    def test_successor_reopen_observes_both_edge_actions_without_tapping(self) -> None:
        state = successor_state()
        authority = driver.expected_successor_action_contracts(driver.SPEC, state)
        expected = driver.expected_successor_action_ids(driver.SPEC, state)
        self.assertEqual(
            {"before-run.edge.spend", "before-run.edge.regain"},
            set(authority),
        )
        for contract in authority.values():
            self.assertEqual(
                "sr5-table-action-" + contract["actionDigest"][7:19],
                contract["automationId"],
            )
        actions = [
            driver.shared.UiNode({"resource-id": automation_id})
            for automation_id in expected
        ]
        device = mock.Mock(spec=driver.shared.Device)
        device.hierarchy.return_value = actions
        with (
            mock.patch.object(driver.physical.shared, "reset_scroll_to_top"),
            mock.patch.object(driver.time, "sleep"),
        ):
            observed = driver.observe_successor_actions(device, driver.SPEC, state)
        self.assertEqual(sorted(expected), observed)
        device.shell.assert_not_called()

    def test_successor_edge_catalog_rejects_arbitrary_mixed_missing_extra_duplicate_and_type_confusion(self) -> None:
        state = successor_state()
        expected = sorted(driver.expected_successor_action_ids(driver.SPEC, state))
        foreign_one = "sr5-table-action-aaaaaaaaaaaa"
        foreign_two = "sr5-table-action-bbbbbbbbbbbb"
        self.assertNotIn(foreign_one, expected)
        self.assertNotIn(foreign_two, expected)
        hostile_catalogs: tuple[tuple[str, list[object]], ...] = (
            ("arbitrary same count", [foreign_one, foreign_two]),
            ("mixed expected and foreign", [expected[0], foreign_one]),
            ("missing", [expected[0]]),
            ("extra", [*expected, foreign_one]),
            ("duplicate", [expected[0], expected[0], expected[1]]),
            ("type confusion", [expected[0], 123]),
        )
        for label, resource_ids in hostile_catalogs:
            with self.subTest(label=label):
                device = mock.Mock(spec=driver.shared.Device)
                device.hierarchy.return_value = [
                    driver.shared.UiNode({"resource-id": resource_id})
                    for resource_id in resource_ids
                ]
                with (
                    mock.patch.object(driver.physical.shared, "reset_scroll_to_top"),
                    mock.patch.object(driver.time, "sleep"),
                    self.assertRaises(RuntimeError),
                ):
                    driver.observe_successor_actions(device, driver.SPEC, state)
                device.shell.assert_not_called()

    def test_missing_disposable_device_authority_fails_before_manifest_or_adb(self) -> None:
        args = SimpleNamespace(
            allow_destructive_disposable_device=False,
            serial="physical-serial",
        )
        with (
            mock.patch.object(driver, "load_and_verify_manifest") as provenance,
            self.assertRaisesRegex(RuntimeError, driver.DISPOSABLE_DEVICE_FLAG),
        ):
            driver.execute_lane(
                args,
                {},
                spec=driver.SPEC,
                driver=DRIVER,
                fixture_validator=driver.require_before_run_fixture,
                before_validator=driver.assert_before_state,
                after_validator=driver.assert_after_state,
                lane_source_paths=driver.before_run_source_paths,
            )
        provenance.assert_not_called()


if __name__ == "__main__":
    unittest.main()
