import ast
from pathlib import Path
import unittest
import uuid
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests/run_api36_career_skill_specialization_e2e.py"
FIXTURE = REPO / "tests/fixtures/career-skill-specialization-e2e.chum5"
SKILL_ID = "11111111-1111-1111-1111-111111111111"
SOURCE_SKILL_ID = "ae91a8a6-80e7-4f52-b9eb-21725a5528a4"
KNOWLEDGE_SKILL_ID = "33333333-3333-3333-3333-333333333333"


class Api36CareerSkillSpecializationDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_digest_revision_and_two_restart_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertIn('"SkillControl.btnAddSpec"', source)
        self.assertIn('"typedCustomKnowledgeIdentityPreserved"', source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "career-skill-specialization"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('abi != "x86_64"', source)
        self.assertNotIn('"profile": "tablet"', source)
        self.assertIn('"careerSkillSpecializationRulesSha256"', source)
        self.assertIn('"careerSkillSpecializationMutationSha256"', source)
        self.assertIn('"careerSkillSpecializationSourceResolverSha256"', source)
        self.assertIn('"presenterPersistenceSha256"', source)
        self.assertIn('"workspaceStoreSha256"', source)
        self.assertIn("saved.content_revision != imported.content_revision + 1", source)
        self.assertIn("saved.payload_sha256 == imported.payload_sha256", source)
        self.assertEqual(2, source.count("shared.force_stop_and_launch_new_process"))
        self.assertIn("shared.require_restored_authority(saved, first_restored)", source)
        self.assertIn("shared.require_restored_authority(saved, second_restored)", source)
        self.assertIn('device.tap("Cancel"', source)
        self.assertIn('device.tap("Buy specialization"', source)
        self.assertIn('"Learned Specialization Pilot Ground Craft (Bike)"', source)
        self.assertIn('"AddSpecialization"', source)
        self.assertIn('"objectid": specialization_id', source)

    def test_inventory_contract_names_both_typed_legacy_rows_and_exact_delta(self) -> None:
        module = ast.parse(DRIVER.read_text(encoding="utf-8"))
        assignments = {
            node.targets[0].id: ast.literal_eval(node.value)
            for node in module.body
            if isinstance(node, ast.Assign)
            and len(node.targets) == 1
            and isinstance(node.targets[0], ast.Name)
            and node.targets[0].id.startswith("INVENTORY_")
        }
        self.assertEqual(
            (
                "Chummer/Controls/Skills/KnowledgeSkillControl.cs::"
                "Chummer.UI.Skills.KnowledgeSkillControl::btnAddSpec",
                "Chummer/Controls/Skills/SkillControl.cs::"
                "Chummer.UI.Skills.SkillControl::btnAddSpec",
            ),
            assignments["INVENTORY_ROW_IDS"],
        )
        self.assertEqual(
            "partial_create_only",
            assignments["INVENTORY_PHONE_STATUS_BEFORE"],
        )
        self.assertEqual(
            "implemented_pending_emulator",
            assignments["INVENTORY_PHONE_STATUS_AFTER"],
        )
        self.assertEqual(
            {
                "implemented_pending_emulator": 2,
                "partial_create_only": -2,
            },
            assignments["INVENTORY_PHONE_STATUS_COUNT_DELTA"],
        )
        source = DRIVER.read_text(encoding="utf-8")
        self.assertIn('"tabletStatusDelta": {}', source)
        self.assertIn('"familyCountDelta": {}', source)
        self.assertIn('"rowCountDelta": 0', source)
        self.assertIn('"completionProvenCountDelta": 0', source)

    def test_fixture_has_exact_typed_guid_source_balance_and_nested_authority(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        self.assertEqual("True", root.findtext("created"))
        self.assertEqual("CareerSkillSpecializationE2E", root.findtext("alias"))
        self.assertEqual("20", root.findtext("karma"))
        skills = root.findall("./newskills/skills/skill")
        self.assertEqual(1, len(skills))
        skill = skills[0]
        self.assertEqual(SKILL_ID, skill.findtext("guid"))
        self.assertEqual(SOURCE_SKILL_ID, skill.findtext("suid"))
        uuid.UUID(skill.findtext("guid"))
        uuid.UUID(skill.findtext("suid"))
        self.assertEqual("False", skill.findtext("isknowledge"))
        self.assertEqual("2", skill.findtext("base"))
        self.assertEqual("1", skill.findtext("karma"))
        self.assertEqual("Vehicle Active", skill.findtext("skillcategory"))
        self.assertIsNone(skill.find("specs"))
        knowledge_skills = root.findall("./newskills/knoskills/skill")
        self.assertEqual(1, len(knowledge_skills))
        knowledge = knowledge_skills[0]
        self.assertEqual(KNOWLEDGE_SKILL_ID, knowledge.findtext("guid"))
        self.assertEqual(
            "00000000-0000-0000-0000-000000000000",
            knowledge.findtext("suid"),
        )
        uuid.UUID(knowledge.findtext("guid"))
        self.assertEqual("True", knowledge.findtext("isknowledge"))
        self.assertEqual("Zoology", knowledge.findtext("name"))
        self.assertEqual("Academic", knowledge.findtext("type"))
        self.assertEqual("2", knowledge.findtext("base"))
        self.assertEqual("0", knowledge.findtext("karma"))
        self.assertEqual("custom-knowledge-must-survive", knowledge.findtext("notes"))
        self.assertIsNone(knowledge.find("specs"))
        self.assertEqual(
            "keep-nested-structure",
            root.findtext("./customstate/sentinel"),
        )


if __name__ == "__main__":
    unittest.main()
