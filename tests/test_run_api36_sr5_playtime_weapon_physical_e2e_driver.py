from __future__ import annotations

import ast
import copy
import importlib.util
from pathlib import Path
import sys
import unittest
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

    def test_shared_driver_contract_covers_review_restart_apply_receipt_ack_successor(self) -> None:
        shared_source = Path(driver.lane.__file__).read_text(encoding="utf-8")
        for marker in (
            "read_transaction(device, spec.checkpoint_key)",
            'phase=0,\n        require_receipt=False',
            '"sr5-table-wizard-resume-review"',
            '"sr5-table-wizard-confirm"',
            'if saved.content_revision != imported.content_revision + 1:',
            '"sr5-table-wizard-receipt-acknowledge"',
            'device.capture(f"sr5-{spec.lane}-saved-successor-reopened")',
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, shared_source)

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
