import importlib.util
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
RESOURCE = ROOT / "src/Chummer.Android/Resources/Localization"


class SkillCatalogStringsTests(unittest.TestCase):
    def setUp(self):
        self.en = {r.get("name"): r.findtext("value") for r in ET.parse(RESOURCE / "SkillCatalogStrings.resx").getroot().findall("data")}
        self.de = {r.get("name"): r.findtext("value") for r in ET.parse(RESOURCE / "SkillCatalogStrings.de.resx").getroot().findall("data")}

    def test_all_pinned_skill_names_have_german_display(self):
        names = {k: v for k, v in self.en.items() if k.startswith("Skill.") and k.endswith(".Name")}
        self.assertEqual(len(names), 271)
        self.assertTrue(all(self.de.get(k) for k in names))
        self.assertEqual(set(self.de) - set(self.en), set())

    def test_legacy_renamed_rows_do_not_change_sr5_meaning(self):
        translated = {v: self.de[k] for k, v in self.en.items() if k.endswith(".Name")}
        self.assertEqual(translated["Navigation"], "Navigation")
        self.assertEqual(translated["Running"], "Laufen")
        self.assertEqual(translated["Swimming"], "Schwimmen")
        self.assertEqual(translated["Etiquette"], "Etikette")
        self.assertEqual(translated["Aeronautics Mechanic"], "Luftfahrtmechanik")

    def test_groups_and_categories_are_covered(self):
        for prefix, expected in (("Group.", 15), ("Category.", 13)):
            keys = [k for k in self.en if k.startswith(prefix)]
            self.assertEqual(len(keys), expected)
            self.assertTrue(all(self.de.get(k) for k in keys))

    def test_generated_keys_are_unique_and_no_translated_rule_payload_exists(self):
        for suffix in ("", ".de"):
            rows = ET.parse(RESOURCE / f"SkillCatalogStrings{suffix}.resx").getroot().findall("data")
            self.assertEqual(len(rows), len({row.get("name") for row in rows}))
            self.assertTrue(all(len(row.findtext("value")) < 200 for row in rows))
            self.assertTrue(all(row.get("name").startswith(("Skill.", "Group.", "Category.")) for row in rows))

    def test_generator_hashes_specializations_by_exact_label(self):
        spec = importlib.util.spec_from_file_location("skill_strings", ROOT / "scripts/materialize_skill_catalog_strings.py")
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        self.assertNotEqual(module.token("Fixed Wing"), module.token("fixed wing"))
        self.assertNotEqual(module.token("A/B"), module.token("A B"))


if __name__ == "__main__":
    unittest.main()
