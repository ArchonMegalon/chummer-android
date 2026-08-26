import ast
import copy
import importlib.util
from pathlib import Path
import sys
import unittest
import uuid
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests/run_api36_career_active_skill_advance_e2e.py"
FIXTURE = REPO / "tests/fixtures/career-active-skill-advance-e2e.chum5"
SKILL_ID = "11111111-1111-1111-1111-111111111111"
SOURCE_SKILL_ID = "ae91a8a6-80e7-4f52-b9eb-21725a5528a4"

sys.path.insert(0, str(DRIVER.parent))
SPEC = importlib.util.spec_from_file_location("career_active_skill_driver", DRIVER)
assert SPEC is not None and SPEC.loader is not None
driver = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(driver)


class Api36CareerActiveSkillAdvanceDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_digest_revision_and_two_restart_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertIn('"SkillControl.btnCareerIncrease"', source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "career-active-skill-advance"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('abi != "x86_64"', source)
        self.assertNotIn('"profile": "tablet"', source)
        self.assertIn('"careerActiveSkillRulesSha256"', source)
        self.assertIn('"careerActiveSkillMutationSha256"', source)
        self.assertIn('"activeSkillSourceResolverSha256"', source)
        self.assertIn('"presenterPersistenceSha256"', source)
        self.assertIn('"workspaceStoreSha256"', source)
        self.assertIn("saved.content_revision != imported.content_revision + 1", source)
        self.assertIn("saved.payload_sha256 == imported.payload_sha256", source)
        self.assertEqual(2, source.count("shared.force_stop_and_launch_new_process"))
        self.assertIn("shared.require_restored_authority(saved, first_restored)", source)
        self.assertIn("shared.require_restored_authority(saved, second_restored)", source)
        self.assertIn('device.tap("Cancel"', source)
        self.assertIn('device.tap("Advance"', source)
        self.assertIn('"Active Skill Pilot Ground Craft 3 -> 4"', source)
        self.assertIn('"ImproveSkill"', source)

    def test_confirm_waits_for_success_notice_before_reading_saved_authority(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        confirm = source.index('device.tap("Advance"')
        success = source.index("ADVANCE_SUCCESS_NOTICE", confirm)
        saved_authority = source.index("saved = read_saved_authority(device)", success)

        self.assertEqual(
            "Active skill advanced and Karma expense saved.",
            driver.ADVANCE_SUCCESS_NOTICE,
        )
        self.assertIn(
            "ADVANCE_SUCCESS_NOTICE,\n        timeout=180,\n        scroll=True,",
            source,
        )
        self.assertLess(confirm, success)
        self.assertLess(success, saved_authority)
        self.assertNotIn(
            'device.wait("build-career-active-skill", timeout=180',
            source,
        )

    def test_fixture_has_exact_guid_source_balance_expense_and_nested_authority(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        driver.require_canonical_import_fixture(root)
        self.assertEqual("True", root.findtext("created"))
        self.assertEqual("CareerActiveSkillAdvanceE2E", root.findtext("alias"))
        self.assertEqual("Human", root.findtext("metatype"))
        self.assertEqual("Priority", root.findtext("buildmethod"))
        self.assertEqual("5.225.0", root.findtext("createdversion"))
        self.assertEqual("5.225.0", root.findtext("appversion"))
        self.assertEqual("SR5", root.findtext("gameedition"))
        self.assertEqual(
            "223a11ff-80e0-428b-89a9-6ef1c243b8b6",
            root.findtext("settings"),
        )
        self.assertEqual("20", root.findtext("karma"))
        skills = root.findall("./newskills/skills/skill")
        self.assertEqual(1, len(skills))
        skill = skills[0]
        self.assertEqual(SKILL_ID, skill.findtext("guid"))
        self.assertEqual(SOURCE_SKILL_ID, skill.findtext("suid"))
        uuid.UUID(skill.findtext("guid"))
        uuid.UUID(skill.findtext("suid"))
        self.assertEqual("2", skill.findtext("base"))
        self.assertEqual("1", skill.findtext("karma"))
        self.assertEqual("Vehicle Active", skill.findtext("skillcategory"))
        self.assertEqual(
            "keep-nested-structure",
            root.findtext("./customstate/sentinel"),
        )

    def test_fixture_preflight_rejects_every_missing_canonical_loader_field(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        for field in driver.CANONICAL_IMPORT_FIELDS:
            with self.subTest(field=field):
                hostile = copy.deepcopy(root)
                hostile.remove(hostile.find(field))
                with self.assertRaisesRegex(
                    RuntimeError,
                    rf"canonical SR5 loader: <{field}>",
                ):
                    driver.require_canonical_import_fixture(hostile)


if __name__ == "__main__":
    unittest.main()
