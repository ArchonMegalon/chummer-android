from __future__ import annotations

import ast
import copy
import importlib.util
from pathlib import Path
import sys
import unittest
from unittest import mock
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
DRIVER = ROOT / "tests/run_api36_sr5_playtime_weapon_physical_e2e.py"
FIXTURE = ROOT / "tests/fixtures/sr5-playtime-weapon-physical-e2e.chum5"
sys.path.insert(0, str(DRIVER.parent))
SPEC = importlib.util.spec_from_file_location("playtime_physical_driver", DRIVER)
assert SPEC is not None and SPEC.loader is not None
driver = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = driver
SPEC.loader.exec_module(driver)


def reviewed_playtime_transaction() -> dict[str, object]:
    contract = driver.lane.expected_action_contract(
        driver.SPEC,
        "workspace-playtime",
        41,
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
            "Lane": 1,
            "WorkspaceId": "workspace-playtime",
            "WorkspaceRevision": 41,
            "SnapshotDigest": contract["snapshotDigest"],
            "SelectedAction": identity,
        },
        "Quote": copy.deepcopy(contract["quote"]),
        "ExpectedPostconditionDigest": contract["postcondition"],
        "Receipt": None,
        "JournalDigest": "",
    }
    transaction["IdempotencyKey"] = driver.lane.length_prefixed_hash(
        "chummer.android.sr5-table-transaction-idempotency/v1",
        transaction["OwnerId"],
        transaction["TransactionId"],
        "workspace-playtime",
        41,
        contract["snapshotDigest"],
        identity["ActionDigest"],
        contract["postcondition"],
    )
    transaction["JournalDigest"] = driver.lane.expected_journal_digest(transaction)
    return transaction


def successor_state() -> dict[str, object]:
    root = ET.parse(FIXTURE).getroot()
    preserved = driver.assert_before_state(root)
    saved = copy.deepcopy(root)
    driver.weapon.active_clip(saved).find("count").text = "8"  # type: ignore[union-attr]
    driver.weapon.linked_ammo(saved).find("qty").text = "8"  # type: ignore[union-attr]
    return driver.assert_after_state(saved, preserved)


class PlaytimePhysicalDriverContractTests(unittest.TestCase):
    def test_driver_is_a_separate_nonrelease_physical_journey(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertEqual("sr5-playtime-weapon-physical", driver.SPEC.journey)
        self.assertEqual(1, driver.SPEC.lane_value)
        self.assertEqual(2, driver.SPEC.action_kind)
        self.assertIn("run_main(", source)
        self.assertIn("Fire one exact three-round Short Burst", source)
        self.assertNotIn("device =", source)
        self.assertNotIn("subprocess", source)
        self.assertNotIn("publish", source.casefold())

    def test_scope_is_one_direct_short_burst_and_not_general_playtime(self) -> None:
        self.assertIn("11 -> 8", driver.SPEC.representative_action)
        for excluded in (
            "damage",
            "conditions",
            "temporary modifiers",
            "initiative",
            "run state",
            "indirect or vehicle weapon fire",
            "tablet",
        ):
            self.assertIn(excluded, driver.SPEC.excluded_scope)

    def test_fixture_exposes_one_short_burst_authority_and_exact_linked_ammo(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        driver.require_playtime_fixture(root)
        target = driver.weapon.target_weapon(root)
        self.assertEqual("BF", target.findtext("mode"))
        self.assertEqual("True", target.findtext("allowshortburst"))
        for field in (
            "allowsingleshot",
            "allowlongburst",
            "allowfullburst",
            "allowsuppressive",
        ):
            self.assertEqual("False", target.findtext(field))
        self.assertEqual("11", driver.weapon.active_clip(root).findtext("count"))
        self.assertEqual("11", driver.weapon.linked_ammo(root).findtext("qty"))
        self.assertEqual("0", root.findtext("edgeused"))
        self.assertEqual("0", root.findtext("./attributes/attribute/totalvalue"))

    def test_after_state_accepts_only_exact_three_round_delta_and_preserved_xml(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        preserved = driver.assert_before_state(root)
        saved = copy.deepcopy(root)
        driver.weapon.active_clip(saved).find("count").text = "8"  # type: ignore[union-attr]
        driver.weapon.linked_ammo(saved).find("qty").text = "8"  # type: ignore[union-attr]
        driver.assert_after_state(saved, preserved)
        hostile = copy.deepcopy(saved)
        driver.weapon.active_clip(hostile).find("count").text = "7"  # type: ignore[union-attr]
        with self.assertRaisesRegex(RuntimeError, "three-round"):
            driver.assert_after_state(hostile, preserved)

        mixed = copy.deepcopy(saved)
        mixed.find("karma").text = "18"  # type: ignore[union-attr]
        with self.assertRaisesRegex(RuntimeError, "outside the exact"):
            driver.assert_after_state(mixed, preserved)

        extraneous = copy.deepcopy(saved)
        ET.SubElement(extraneous, "pass-shaped-extra").text = "unexpected"
        with self.assertRaisesRegex(RuntimeError, "outside the exact"):
            driver.assert_after_state(extraneous, preserved)

    def test_review_binds_exact_weapon_identity_mode_plan_delta_and_journal(self) -> None:
        transaction = reviewed_playtime_transaction()
        self.assertIsNone(
            driver.lane.validate_transaction(
                transaction,
                spec=driver.SPEC,
                workspace_id="workspace-playtime",
                expected_revision=41,
                phase=0,
                version=1,
                require_receipt=False,
            )
        )
        identity = transaction["Quote"]["Identity"]
        plan = transaction["Quote"]["WeaponPlan"]
        self.assertEqual("playtime.weapon.fire", identity["ActionId"])
        self.assertEqual(driver.weapon.WEAPON_ID, identity["WeaponId"])
        self.assertEqual(driver.weapon.AMMO_SLOT, identity["AmmoSlot"])
        self.assertEqual(1, identity["FireMode"])
        self.assertEqual(
            {
                "Mode": 1,
                "RoundsConsumed": 3,
                "NewAmmoRemaining": 8,
                "NewAmmoGearQuantity": 8,
                "DeleteAmmoGear": False,
                "RequiresPartialConfirmation": False,
            },
            plan,
        )

    def test_weapon_identity_and_every_plan_delta_field_fail_closed(self) -> None:
        hostile_paths = (
            (("Quote", "Identity", "ActionId"), "playtime.edge.spend"),
            (("Quote", "Identity", "Kind"), 0),
            (("Quote", "Identity", "WeaponId"), "f3333333-3333-4333-8333-333333333333"),
            (("Quote", "Identity", "AmmoSlot"), 2),
            (("Quote", "Identity", "AmmoGearId"), driver.lane.EMPTY_GUID),
            (("Quote", "Identity", "FireMode"), 0),
            (("Review", "SelectedAction", "FireMode"), 0),
            (("Quote", "WeaponPlan", "Mode"), 0),
            (("Quote", "WeaponPlan", "Mode"), True),
            (("Quote", "WeaponPlan", "RoundsConsumed"), 2),
            (("Quote", "WeaponPlan", "NewAmmoRemaining"), 9),
            (("Quote", "WeaponPlan", "NewAmmoGearQuantity"), 9),
            (("Quote", "WeaponPlan", "DeleteAmmoGear"), True),
            (("Quote", "WeaponPlan", "RequiresPartialConfirmation"), True),
            (("Quote", "WeaponPlan"), None),
        )
        for path, value in hostile_paths:
            with self.subTest(path=path):
                hostile = reviewed_playtime_transaction()
                target: object = hostile
                for field in path[:-1]:
                    assert isinstance(target, dict)
                    target = target[field]
                assert isinstance(target, dict)
                target[path[-1]] = value
                with self.assertRaises(RuntimeError):
                    driver.lane.validate_transaction(
                        hostile,
                        spec=driver.SPEC,
                        workspace_id="workspace-playtime",
                        expected_revision=41,
                        phase=0,
                        version=1,
                        require_receipt=False,
                    )

    def test_shared_driver_contract_covers_review_restart_apply_receipt_ack_successor(self) -> None:
        shared_source = Path(driver.lane.__file__).read_text(encoding="utf-8")
        for marker in (
            "read_transaction(device, spec.checkpoint_key)",
            'phase=0,\n        version=1,\n        require_receipt=False',
            '"sr5-table-wizard-resume-review"',
            '"sr5-table-wizard-confirm"',
            'if saved.content_revision != imported.content_revision + 1:',
            '"sr5-table-wizard-receipt-acknowledge"',
            "observe_successor_actions(device, spec, final_successor_state)",
            'device.capture(f"sr5-{spec.lane}-saved-successor-reopened")',
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, shared_source)
        self.assertNotIn("successor_action = tap_unique_typed_action", shared_source)

    def test_successor_reopen_observes_exact_next_short_burst_without_tapping(self) -> None:
        state = successor_state()
        authority = driver.lane.expected_successor_action_contracts(driver.SPEC, state)
        self.assertEqual({"playtime.weapon.fire"}, set(authority))
        contract = authority["playtime.weapon.fire"]
        self.assertEqual(8, contract["ammoBefore"])
        self.assertEqual(5, contract["ammoAfter"])
        self.assertEqual(3, contract["roundsConsumed"])
        self.assertEqual(
            driver.lane.expected_weapon_target_revision(8),
            contract["targetRevision"],
        )
        self.assertEqual(
            "sr5-table-action-" + contract["actionDigest"][7:19],
            contract["automationId"],
        )
        expected = driver.lane.expected_successor_action_ids(driver.SPEC, state)
        device = mock.Mock(spec=driver.lane.shared.Device)
        device.hierarchy.return_value = [
            driver.lane.shared.UiNode({"resource-id": automation_id})
            for automation_id in expected
        ]
        with (
            mock.patch.object(driver.lane.shared, "reset_scroll_to_top"),
            mock.patch.object(driver.lane.time, "sleep"),
        ):
            observed = driver.lane.observe_successor_actions(device, driver.SPEC, state)
        self.assertEqual(sorted(expected), observed)
        device.shell.assert_not_called()

    def test_successor_playtime_catalog_rejects_arbitrary_mixed_missing_extra_duplicate_and_type_confusion(self) -> None:
        state = successor_state()
        expected = next(iter(driver.lane.expected_successor_action_ids(driver.SPEC, state)))
        foreign = "sr5-table-action-cccccccccccc"
        self.assertNotEqual(foreign, expected)
        hostile_catalogs: tuple[tuple[str, list[object]], ...] = (
            ("arbitrary same count", [foreign]),
            ("mixed expected and foreign", [expected, foreign]),
            ("missing", []),
            ("extra", [expected, foreign]),
            ("duplicate", [expected, expected]),
            ("type confusion", [123]),
        )
        for label, resource_ids in hostile_catalogs:
            with self.subTest(label=label):
                device = mock.Mock(spec=driver.lane.shared.Device)
                device.hierarchy.return_value = [
                    driver.lane.shared.UiNode({"resource-id": resource_id})
                    for resource_id in resource_ids
                ]
                with (
                    mock.patch.object(driver.lane.shared, "reset_scroll_to_top"),
                    mock.patch.object(driver.lane.time, "sleep"),
                    self.assertRaises(RuntimeError),
                ):
                    driver.lane.observe_successor_actions(device, driver.SPEC, state)
                device.shell.assert_not_called()

    def test_playtime_source_graph_binds_typed_weapon_request_rules_and_helper(self) -> None:
        paths = driver.playtime_source_paths(
            Path("/core"),
            Path("/presentation"),
        )
        self.assertEqual(
            Path("/presentation/Chummer.Presentation/Overview/CareerWeaponFireRequest.cs"),
            paths["careerWeaponRequestSha256"],
        )
        self.assertEqual(
            Path("/core/Chummer.Contracts/Characters/CharacterWeaponFireRules.cs"),
            paths["careerWeaponRulesSha256"],
        )
        self.assertEqual(
            Path(driver.weapon.__file__).resolve(),
            paths["weaponFixtureAuthorityHelperSha256"],
        )


if __name__ == "__main__":
    unittest.main()
