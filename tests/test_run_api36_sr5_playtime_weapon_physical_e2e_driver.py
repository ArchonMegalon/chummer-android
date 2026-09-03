from __future__ import annotations

import ast
import copy
import hashlib
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
import api36_proof_state

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
    def test_import_uses_exact_career_route_workspace_and_payload_not_visible_alias(self) -> None:
        fixture_payload = FIXTURE.read_text(encoding="utf-8")
        fixture_sha256 = hashlib.sha256(fixture_payload.encode("utf-8")).hexdigest()
        pending = driver.lane.shared.WorkspaceAuthority(
            "workspace-playtime",
            1,
            0,
            fixture_sha256,
            "d" * 64,
        )
        checkpointed = pending.__class__(
            pending.workspace_id,
            1,
            1,
            pending.payload_sha256,
            pending.document_sha256,
        )
        expectation = object()
        trace: list[dict[str, object]] = []
        launch = object()
        import_proof = object()
        device = mock.Mock(spec=driver.lane.shared.Device)

        with (
            mock.patch.object(driver.lane.shared, "launch_app", return_value=launch),
            mock.patch.object(driver.lane.shared, "wait_for_phone_runners") as runners,
            mock.patch.object(driver.lane.shared, "record_phone_ui_locale_evidence"),
            mock.patch.object(driver.lane.shared, "select_android_document") as select,
            mock.patch.object(
                api36_proof_state,
                "wait_for_import_activation",
                return_value=import_proof,
            ) as import_activation,
            mock.patch.object(
                driver.lane.shared,
                "wait_for_phone_runner_route",
            ) as career_runner,
            mock.patch.object(driver.lane.shared, "tap_phone_destination") as destination,
            mock.patch.object(driver.lane.shared, "open_build") as open_build,
            mock.patch.object(
                driver.lane.shared,
                "save_runner_and_wait_for_durable_notice",
            ) as save_runner,
            mock.patch.object(
                driver.lane,
                "read_proof_workspace_authority",
                side_effect=[pending, checkpointed],
            ) as workspace,
            mock.patch.object(
                driver.lane,
                "workspace_payloads",
                return_value=[fixture_payload],
            ),
        ):
            observed = driver.lane.prepare_runner(
                device,
                driver.SPEC,
                FIXTURE.name,
                fixture_sha256,
                expectation,
                trace,
            )

        self.assertIs(launch, observed[0])
        self.assertEqual(pending, observed[1])
        self.assertEqual(checkpointed, observed[2])
        self.assertIs(import_proof, observed[3])
        self.assertEqual(driver.FIXTURE_ALIAS, observed[4].findtext("alias"))
        device.wait.assert_not_called()
        device.tap.assert_called_once_with("home-open-file")
        select.assert_called_once_with(device, FIXTURE.name)
        import_activation.assert_called_once_with(
            device,
            expected=expectation,
            content_sha256=fixture_sha256,
            timeout=120,
        )
        career_runner.assert_called_once_with(device, created=True, timeout=120)
        self.assertEqual(
            [
                mock.call(device, "phone-destination-runners"),
                mock.call(device, "phone-destination-runners"),
            ],
            destination.call_args_list,
        )
        self.assertEqual(3, runners.call_count)
        open_build.assert_called_once_with(device, "phone")
        save_runner.assert_called_once_with(device)
        self.assertEqual(
            [
                mock.call(
                    device,
                    expectation,
                    trace,
                    label="imported-runner-pending-save",
                    page_automation_id="phone-runners",
                    stage="runners-ready",
                    wizard_lane=None,
                ),
                mock.call(
                    device,
                    expectation,
                    trace,
                    label="imported-runner-checkpointed",
                    page_automation_id="phone-runners",
                    stage="runners-ready",
                    wizard_lane=None,
                ),
            ],
            workspace.call_args_list,
        )

    def test_import_checkpoint_requires_exact_one_zero_to_one_one_identity_transition(self) -> None:
        pending = driver.lane.shared.WorkspaceAuthority(
            "workspace-playtime", 1, 0, "a" * 64, "b" * 64
        )
        checkpointed = pending.__class__(
            pending.workspace_id, 1, 1, pending.payload_sha256, pending.document_sha256
        )
        driver.lane.require_exact_pending_import_checkpoint(pending)
        driver.lane.require_exact_import_checkpoint_transition(pending, checkpointed)

        hostile_pending = (
            pending.__class__("workspace-playtime", 1, 1, "a" * 64, "b" * 64),
            pending.__class__("workspace-playtime", 2, 1, "a" * 64, "b" * 64),
        )
        for hostile in hostile_pending:
            with self.subTest(hostile=hostile), self.assertRaises(RuntimeError):
                driver.lane.require_exact_pending_import_checkpoint(hostile)

        hostile_saved = (
            checkpointed.__class__("other", 1, 1, "a" * 64, "b" * 64),
            checkpointed.__class__("workspace-playtime", 2, 2, "a" * 64, "b" * 64),
            checkpointed.__class__("workspace-playtime", 1, 0, "a" * 64, "b" * 64),
            checkpointed.__class__("workspace-playtime", 1, 1, "c" * 64, "b" * 64),
            checkpointed.__class__("workspace-playtime", 1, 1, "a" * 64, "c" * 64),
        )
        for hostile in hostile_saved:
            with self.subTest(hostile=hostile), self.assertRaises(RuntimeError):
                driver.lane.require_exact_import_checkpoint_transition(pending, hostile)

    def test_black_box_lane_checkpoints_import_once_before_returning_runner(self) -> None:
        fixture_payload = FIXTURE.read_text(encoding="utf-8")
        fixture_sha256 = hashlib.sha256(fixture_payload.encode("utf-8")).hexdigest()
        pending = driver.lane.shared.WorkspaceAuthority(
            "workspace-playtime", 1, 0, fixture_sha256, "d" * 64
        )
        checkpointed = pending.__class__(
            pending.workspace_id, 1, 1, pending.payload_sha256, pending.document_sha256
        )
        device = mock.Mock(spec=driver.lane.shared.Device)
        events: list[str] = []

        with (
            mock.patch.object(driver.lane.shared, "launch_app", return_value=object()),
            mock.patch.object(driver.lane.shared, "wait_for_phone_runners"),
            mock.patch.object(driver.lane.shared, "record_phone_ui_locale_evidence"),
            mock.patch.object(driver.lane.shared, "select_android_document"),
            mock.patch.object(driver.lane.shared, "wait_for_phone_runner_route"),
            mock.patch.object(driver.lane.shared, "tap_phone_destination"),
            mock.patch.object(
                driver.lane.shared,
                "read_phone_workspace_authority",
                return_value=pending,
            ) as read_pending,
            mock.patch.object(
                driver.lane.shared,
                "read_workspace_authority",
                side_effect=lambda _device: events.append("read-checkpointed")
                or checkpointed,
            ) as read_checkpointed,
            mock.patch.object(
                driver.lane.shared,
                "open_build",
                side_effect=lambda *_args: events.append("open-build"),
            ) as open_build,
            mock.patch.object(
                driver.lane.shared,
                "save_runner_and_wait_for_durable_notice",
                side_effect=lambda *_args: events.append("save"),
            ) as save_runner,
            mock.patch.object(
                driver.lane,
                "workspace_payloads",
                return_value=[fixture_payload],
            ),
        ):
            observed = driver.lane.prepare_runner(
                device,
                driver.SPEC,
                FIXTURE.name,
                fixture_sha256,
            )

        self.assertEqual(pending, observed[1])
        self.assertEqual(checkpointed, observed[2])
        read_pending.assert_called_once_with(device)
        open_build.assert_called_once_with(device, "phone")
        save_runner.assert_called_once_with(device)
        read_checkpointed.assert_called_once_with(device)
        self.assertEqual(["open-build", "save", "read-checkpointed"], events)

    def test_product_table_authority_and_every_review_gate_require_clean_saved_revision(self) -> None:
        authority = (
            ROOT
            / "src/Chummer.Android/Native/RunnerSessionSr5TableWizardPhoneAuthority.cs"
        ).read_text(encoding="utf-8")
        page = (ROOT / "src/Chummer.Android/Native/Sr5TableWizardPage.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("_coordinator.State.IsDirty", authority)
        self.assertIn(
            "_coordinator.State.SavedRevision != _coordinator.State.ContentRevision",
            authority,
        )
        matches = authority[authority.index("private bool Matches(") :]
        self.assertIn("_coordinator.State.SavedRevision == revision", matches)
        self.assertIn("!_coordinator.State.IsDirty", matches)
        self.assertGreaterEqual(
            page.count("Coordinator.State.SavedRevision == snapshot.WorkspaceRevision"),
            2,
        )
        self.assertIn(
            "Coordinator.State.SavedRevision != snapshot.WorkspaceRevision",
            page,
        )
        self.assertGreaterEqual(page.count("Coordinator.State.IsDirty"), 3)
        self.assertIn('"sr5-table-wizard-save-required"', page)
        self.assertIn("No review or resumable transaction was opened", page)
        self.assertGreaterEqual(page.count("_transaction = null;"), 3)
        self.assertLess(
            page.index(".LoadAsync(_lane, cancellationToken)"),
            page.index("_transactionStore.TryRead"),
        )

    def test_payload_bound_import_rejects_a_different_fixture_alias(self) -> None:
        hostile_root = ET.parse(FIXTURE).getroot()
        hostile_root.find("alias").text = "VisibleButForeignRunner"  # type: ignore[union-attr]
        hostile_payload = ET.tostring(hostile_root, encoding="unicode")
        hostile_sha256 = hashlib.sha256(hostile_payload.encode("utf-8")).hexdigest()
        authority = driver.lane.shared.WorkspaceAuthority(
            "workspace-playtime",
            41,
            41,
            hostile_sha256,
            "d" * 64,
        )
        device = mock.Mock(spec=driver.lane.shared.Device)

        with (
            mock.patch.object(
                driver.lane,
                "workspace_payloads",
                return_value=[hostile_payload],
            ),
            self.assertRaisesRegex(RuntimeError, "different Career fixture"),
        ):
            driver.lane.root_for_authority(
                device,
                authority,
                driver.FIXTURE_ALIAS,
            )

    def test_career_table_navigation_preserves_exact_slash_resource_ids(self) -> None:
        device = mock.Mock(spec=driver.lane.shared.Device)
        with (
            mock.patch.object(driver.lane.physical, "open_career_hub") as career_hub,
            mock.patch.object(driver.lane.physical, "wait_exact_route") as route,
        ):
            driver.lane.open_lane(device, driver.SPEC)

        career_hub.assert_called_once_with(device)
        self.assertEqual(
            [
                mock.call(
                    "sr5-career/table",
                    timeout=90,
                    backward_scrolls=0,
                    forward_scrolls=24,
                    scroll_distance_ratio=0.18,
                    evidence_prefix="sr5-career-table-family",
                    surface_name="SR5 Career table family route",
                ),
                mock.call(
                    "sr5-career-action-playtime",
                    timeout=90,
                    backward_scrolls=0,
                    forward_scrolls=24,
                    scroll_distance_ratio=0.18,
                    evidence_prefix="sr5-playtime-route",
                    surface_name="SR5 playtime typed route",
                ),
            ],
            device.tap_exact_resource_id_bidirectional.call_args_list,
        )
        self.assertEqual(
            [
                mock.call(device, "sr5-career/table", timeout=90),
                mock.call(device, "sr5-career/playtime", timeout=120),
            ],
            route.call_args_list,
        )
        device.wait.assert_not_called()

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
            '"explicitDurableSaveOperations": 1',
            '"checkpointImportedRunnerBeforeTable": "pass"',
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
