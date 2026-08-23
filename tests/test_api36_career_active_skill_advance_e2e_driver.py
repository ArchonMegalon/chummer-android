import ast
from pathlib import Path
import unittest
import uuid
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests/run_api36_career_active_skill_advance_e2e.py"
FIXTURE = REPO / "tests/fixtures/career-active-skill-advance-e2e.chum5"
SKILL_ID = "11111111-1111-1111-1111-111111111111"
SOURCE_SKILL_ID = "ae91a8a6-80e7-4f52-b9eb-21725a5528a4"


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

    def test_fixture_has_exact_guid_source_balance_expense_and_nested_authority(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        self.assertEqual("True", root.findtext("created"))
        self.assertEqual("CareerActiveSkillAdvanceE2E", root.findtext("alias"))
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


if __name__ == "__main__":
    unittest.main()
