import ast
import copy
import importlib.util
from pathlib import Path
import sys
import unittest
from unittest.mock import Mock, patch
import uuid
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests/run_api36_career_weapon_fire_e2e.py"
FIXTURE = REPO / "tests/fixtures/career-weapon-fire-e2e.chum5"
TABLE_WIZARD_PAGE = REPO / "src/Chummer.Android/Native/Sr5TableWizardPage.cs"

sys.path.insert(0, str(DRIVER.parent))
SPEC = importlib.util.spec_from_file_location("career_weapon_fire_driver", DRIVER)
assert SPEC is not None and SPEC.loader is not None
driver = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(driver)


class Api36CareerWeaponFireDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_source_digest_revision_and_new_pid_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertIn('CONTROL = "CharacterCareer.cmsAmmoShortBurst"', source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "career-weapon-fire"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('abi != "x86_64"', source)
        self.assertNotIn('"profile": "tablet"', source)
        for digest in (
            '"careerWeaponFireRequestSha256"',
            '"weaponFireRulesSha256"',
            '"presenterMutationSha256"',
            '"presenterPersistenceSha256"',
            '"workspaceStoreSha256"',
            '"careerWizardPageSha256"',
            '"tableWizardPageSha256"',
            '"tableWizardTransactionSha256"',
            '"tableWizardAuthoritySha256"',
            '"tableWizardSessionSha256"',
        ):
            self.assertIn(digest, source)
        self.assertIn("saved.content_revision != imported.content_revision + 1", source)
        self.assertIn("saved.payload_sha256 == imported.payload_sha256", source)
        self.assertIn("saved.document_sha256 == imported.document_sha256", source)
        self.assertEqual(1, source.count("shared.force_stop_and_launch_new_process"))
        self.assertIn("shared.require_restored_authority(saved, restored)", source)
        self.assertIn('"afterForceStop": list(restart.after_force_stop.process_ids)', source)
        self.assertIn("physical.open_career_hub(device)", source)
        self.assertIn("device.tap_exact_resource_id_bidirectional(", source)
        for legacy_route in (
            "build-section-tab-gear",
            "build-action-tab-gear-weapons",
            "collection-item-weapon-",
            "career-weapon-fire-open-",
        ):
            self.assertNotIn(legacy_route, source)

    def test_short_burst_action_identity_is_digest_derived_and_fail_closed(self) -> None:
        self.assertEqual(
            driver.short_burst_action_automation_id(11),
            "sr5-table-action-b3315e0e390c",
        )
        self.assertNotEqual(
            driver.short_burst_action_automation_id(11),
            driver.short_burst_action_automation_id(8),
        )
        self.assertRegex(driver.short_burst_action_automation_id(11), r"^sr5-table-action-[0-9a-f]{12}$")
        for hostile in (None, True, 2, "11"):
            with self.subTest(hostile=hostile), self.assertRaises(RuntimeError):
                driver.short_burst_action_automation_id(hostile)  # type: ignore[arg-type]

    def test_open_page_uses_only_the_career_table_and_playtime_wizard_routes(self) -> None:
        device = Mock(spec=driver.shared.Device)
        with (
            patch.object(driver.physical, "open_career_hub") as open_hub,
            patch.object(driver.physical, "wait_exact_route") as wait_route,
        ):
            driver.open_page(device)

        open_hub.assert_called_once_with(device)
        self.assertEqual(
            [call.args[0] for call in device.tap_exact_resource_id_bidirectional.call_args_list],
            ["sr5-career/table", "sr5-career-action-playtime"],
        )
        self.assertEqual(
            device.tap_exact_resource_id_bidirectional.call_args_list[0]
            .kwargs["backward_scrolls"],
            24,
        )
        self.assertEqual(
            [call.args[1] for call in wait_route.call_args_list],
            ["sr5-career/table", "sr5-career/playtime"],
        )

    def test_ui_readback_requires_the_exact_digest_action_and_ammo_delta(self) -> None:
        selector = driver.short_burst_action_automation_id(11)
        device = Mock(spec=driver.shared.Device)
        device.wait_exact_resource_id_bidirectional.return_value = driver.shared.UiNode({
            "resource-id": f"com.myexternalbrain.chummer:id/{selector}",
            "content-desc": "Fire · Short Burst. 3 rounds · ammo 11 → 8",
        })
        driver.assert_ui_readback(device, 11)

        device.wait_exact_resource_id_bidirectional.return_value = driver.shared.UiNode({
            "content-desc": "Fire · Short Burst. 3 rounds · ammo 10 → 7",
        })
        with self.assertRaisesRegex(RuntimeError, "not read back exactly"):
            driver.assert_ui_readback(device, 11)

    def test_apply_uses_one_exact_quote_review_confirm_and_receipt_sequence(self) -> None:
        device = Mock(spec=driver.shared.Device)
        with patch.object(driver.physical, "wait_exact_route") as wait_route:
            driver.apply_short_burst(device, 11)

        self.assertEqual(
            [call.args[0] for call in device.tap_single_exact_resource_id.call_args_list],
            [
                driver.short_burst_action_automation_id(11),
                "sr5-table-wizard-open-review",
                "sr5-table-wizard-confirm",
                "sr5-table-wizard-receipt-acknowledge",
            ],
        )
        self.assertEqual(
            [call.args[1] for call in wait_route.call_args_list],
            ["sr5-table-wizard-quote", "sr5-career/playtime/review", "sr5-career/playtime"],
        )
        device.tap.assert_called_once_with("OK", timeout=180)
        device.wait_for_single_exact_resource_id.assert_called_once()

    def test_successful_confirm_does_not_refresh_the_disappeared_review_page(self) -> None:
        source = TABLE_WIZARD_PAGE.read_text(encoding="utf-8")
        self.assertIn(
            "_confirm.Clicked += async (_, _) => await "
            "RunWithConditionalRefreshAsync(ConfirmAsync);",
            source,
        )
        self.assertIn("private async Task<bool> ConfirmAsync()", source)
        self.assertIn(
            "// this disappeared review page would publish a stale review over it.\n"
            "        return false;",
            source,
        )

    def test_fixture_binds_exact_root_weapon_active_clip_linked_ammo_and_burst(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        driver.require_canonical_import_fixture(root)
        preserved = driver.assert_before(root)
        self.assertEqual(driver.WEAPON_ID, driver.target_weapon(root).findtext("guid"))
        self.assertEqual(driver.AMMO_GEAR_ID, driver.active_clip(root).findtext("id"))
        self.assertEqual(driver.AMMO_GEAR_ID, driver.linked_ammo(root).findtext("guid"))
        self.assertEqual("3", driver.target_weapon(root).findtext("shortburst"))
        self.assertEqual("11", driver.active_clip(root).findtext("count"))
        self.assertEqual("11", driver.linked_ammo(root).findtext("qty"))
        self.assertEqual("19", preserved["karma"])
        self.assertEqual("8765", preserved["nuyen"])
        for identity in (
            driver.WEAPON_ID,
            driver.AMMO_GEAR_ID,
            driver.UNRELATED_WEAPON_ID,
            driver.UNRELATED_GEAR_ID,
        ):
            uuid.UUID(identity)

    def test_after_contract_accepts_only_exact_three_round_delta_and_preserved_xml(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        preserved = driver.assert_before(root)
        driver.active_clip(root).find("count").text = str(driver.EXPECTED_AMMO)
        driver.linked_ammo(root).find("qty").text = str(driver.EXPECTED_AMMO)
        driver.assert_after(root, preserved)

        hostile = copy.deepcopy(root)
        driver.target_weapon(hostile).find("notes").text = "changed"
        with self.assertRaisesRegex(RuntimeError, "outside the exact clip/ammo quantities"):
            driver.assert_after(hostile, preserved)

    def test_fixture_preflight_rejects_every_missing_canonical_loader_field(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        for field in driver.CANONICAL_IMPORT_FIELDS:
            with self.subTest(field=field):
                hostile = copy.deepcopy(root)
                hostile.remove(hostile.find(field))
                with self.assertRaisesRegex(RuntimeError, rf"canonical SR5 loader: <{field}>"):
                    driver.require_canonical_import_fixture(hostile)


if __name__ == "__main__":
    unittest.main()
