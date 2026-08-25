import ast
import copy
import importlib.util
from pathlib import Path
import sys
import unittest
import uuid
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests/run_api36_career_weapon_fire_e2e.py"
FIXTURE = REPO / "tests/fixtures/career-weapon-fire-e2e.chum5"

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
        ):
            self.assertIn(digest, source)
        self.assertIn("saved.content_revision != imported.content_revision + 1", source)
        self.assertIn("saved.payload_sha256 == imported.payload_sha256", source)
        self.assertIn("saved.document_sha256 == imported.document_sha256", source)
        self.assertEqual(1, source.count("shared.force_stop_and_launch_new_process"))
        self.assertIn("shared.require_restored_authority(saved, restored)", source)
        self.assertIn('"afterForceStop": list(restart.after_force_stop.process_ids)', source)
        self.assertIn(
            'device.tap_single_exact_resource_id_bidirectional(\n'
            '        "build-action-tab-gear-weapons"',
            source,
        )
        self.assertIn("backward_scrolls=48", source)
        self.assertIn('evidence_prefix="career-weapon-fire-weapons-route"', source)
        self.assertNotIn(
            'device.tap("build-action-tab-gear-weapons", scroll=True',
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
