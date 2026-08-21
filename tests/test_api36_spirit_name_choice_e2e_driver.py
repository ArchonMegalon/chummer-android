import ast
from pathlib import Path
import unittest
import uuid
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_spirit_name_choice_e2e.py"
FIXTURES = (
    REPO / "tests" / "fixtures" / "creation-spirit-name-choice-e2e.chum5",
    REPO / "tests" / "fixtures" / "career-spirit-name-choice-e2e.chum5",
)


class Api36SpiritNameChoiceE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_api36_digest_bound_and_shared_row_exact(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)

        self.assertIn('CONTROL = "SpiritControl.cboSpiritName"', source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "spirit-name-choice"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('"build-section-tab-magician"', source)
        self.assertIn('"build-action-tab-magician-spirits"', source)
        self.assertIn('f"collection-item-spirit-{expected[\'target_id\']}"', source)
        self.assertIn('f"spirit-name-choice-open-{token}"', source)
        self.assertIn('f"spirit-name-choice-picker-{token}"', source)
        self.assertIn('f"spirit-name-choice-save-{token}"', source)
        self.assertGreaterEqual(source.count('device.shell("am", "force-stop"'), 2)
        self.assertIn('"expectedRevisionAtomicSave"', source)
        self.assertIn('"dropDownListOnly"', source)
        self.assertIn('"limitBeforeAddRules"', source)
        self.assertIn('"spiritNameChoiceRulesSha256"', source)
        self.assertIn('"sourceResolverSha256"', source)
        self.assertIn('"traditionsCatalogSha256"', source)
        self.assertIn('"streamsCatalogSha256"', source)
        self.assertIn('"presenterPersistenceSha256"', source)
        self.assertIn('"workspaceStoreSha256"', source)
        self.assertIn('"buildPageSha256"', source)
        self.assertIn('"buildFlowPagesSha256"', source)
        self.assertIn('"sr5ShellCatalogSha256"', source)
        self.assertIn('"creationFixtureSha256"', source)
        self.assertIn('"careerFixtureSha256"', source)
        self.assertIn('"controlCount": 1', source)
        self.assertNotIn('"tablet"', source)

    def test_paired_fixtures_bind_exact_rules_unique_ids_and_unrelated_xml(self) -> None:
        all_ids: set[uuid.UUID] = set()
        created_values: list[str | None] = []
        entity_types: list[str] = []
        for fixture in FIXTURES:
            root = ET.parse(fixture).getroot()
            created_values.append(root.findtext("created"))
            spirits = root.findall("./spirits/spirit")
            self.assertEqual(2, len(spirits))
            identities = [uuid.UUID(spirit.findtext("guid", default="")) for spirit in spirits]
            self.assertEqual(2, len(set(identities)))
            self.assertTrue(all(identity.int != 0 for identity in identities))
            self.assertTrue(all(identity not in all_ids for identity in identities))
            all_ids.update(identities)
            entity_types.append(spirits[0].findtext("type", default=""))
            self.assertTrue(all(spirit.findtext("notes", default="") for spirit in spirits))
            self.assertTrue(root.findtext("./customstate/name", default="").endswith("name text"))
            self.assertIsNotNone(root.find("tradition"))
            self.assertIsNotNone(root.find("improvements"))
            self.assertIsNotNone(root.find("customdatadirectorynames"))

        creation = ET.parse(FIXTURES[0]).getroot()
        creation_improvements = creation.findall("./improvements/improvement")
        self.assertEqual(
            ["1", "True", "False", "1"],
            [item.findtext("enabled") for item in creation_improvements],
        )
        enabled_limits = [
            item.findtext("improvedname")
            for item in creation_improvements
            if item.findtext("improvementttype") == "LimitSpiritCategory"
            and item.findtext("enabled") in {"1", "True"}
        ]
        enabled_additions = [
            item.findtext("improvedname")
            for item in creation_improvements
            if item.findtext("improvementttype") == "AddSpirit"
            and item.findtext("enabled") in {"1", "True"}
        ]
        self.assertEqual(["Spirit of Fire", "Spirit of Water"], enabled_limits)
        self.assertEqual(["Guardian Spirit"], enabled_additions)
        self.assertEqual(
            ["Machine Sprite", "Courier Sprite", "Data Sprite"],
            [item.text for item in ET.parse(FIXTURES[1]).getroot().findall("./tradition/spirits/spirit")],
        )
        career_additions = [
            item.findtext("improvedname")
            for item in ET.parse(FIXTURES[1]).getroot().findall("./improvements/improvement")
            if item.findtext("improvementttype") == "AddSprite"
            and item.findtext("enabled") in {"1", "True"}
        ]
        self.assertEqual(["Diagnostics Sprite"], career_additions)
        self.assertEqual(["False", "True"], created_values)
        self.assertEqual(["Spirit", "Sprite"], entity_types)
        self.assertEqual(4, len(all_ids))


if __name__ == "__main__":
    unittest.main()
