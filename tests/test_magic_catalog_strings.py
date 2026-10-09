from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
RESOURCE = ROOT / "src/Chummer.Android/Resources/Localization"


class MagicCatalogStringsTests(unittest.TestCase):
    def setUp(self):
        self.en = {r.get("name"): r.findtext("value") for r in ET.parse(RESOURCE / "MagicCatalogStrings.resx").getroot().findall("data")}
        self.de = {r.get("name"): r.findtext("value") for r in ET.parse(RESOURCE / "MagicCatalogStrings.de.resx").getroot().findall("data")}

    def test_every_pinned_catalog_name_has_german_display(self):
        for kind, count in (("tradition", 74), ("stream", 1), ("adept-power", 109), ("spell", 363), ("complex-form", 38), ("priority-rank", 35)):
            keys = [key for key in self.en if key.startswith(f"Option.{kind}.")]
            self.assertEqual(len(keys), count)
            self.assertTrue(all(self.de.get(key) for key in keys))
        self.assertEqual(set(self.en), set(self.de))

    def test_labels_only_and_unambiguous_keys(self):
        for suffix in ("", ".de"):
            rows = ET.parse(RESOURCE / f"MagicCatalogStrings{suffix}.resx").getroot().findall("data")
            self.assertEqual(len(rows), len({r.get("name") for r in rows}))
            self.assertTrue(all(0 < len(r.findtext("value")) < 200 for r in rows))
            self.assertTrue(all(r.get("name").startswith(("Option.", "Category.", "Talent.", "Metatype.", "MetatypeCategory.")) for r in rows))

    def test_renamed_legacy_translation_does_not_change_focus_meaning(self):
        keys = [key for key, name in self.en.items() if name == "Disrupt [Focus]"]
        self.assertEqual(len(keys), 1)
        self.assertEqual(self.de[keys[0]], "Fokus stören")

    def test_same_name_spell_variants_keep_separate_ids(self):
        keys = [key for key, name in self.en.items() if name == "Sound Barrier"]
        self.assertEqual(len(keys), 2)
        self.assertNotEqual(keys[0], keys[1])
        self.assertEqual([self.de[key] for key in keys], ["Schallbarriere", "Schallbarriere"])

    def test_talent_and_category_captions_are_covered(self):
        for prefix, count in (("Talent.", 27), ("Category.spell.", 7), ("Metatype.", 21), ("MetatypeCategory.", 4)):
            self.assertEqual(sum(key.startswith(prefix) for key in self.en), count)

    def test_saved_magic_rereview_uses_exact_identity_display_translation(self):
        source = (ROOT / "src/Chummer.Android/Native/CreationMagicReReviewPage.cs").read_text()
        body = source.split("private void AddChoice(", 1)[1].split("private void AddExit()", 1)[0]
        self.assertIn(".Single(row => row.Identity == identity).Name", body)
        self.assertIn("name = MagicCatalogStrings.OptionName(identity.Kind, identity.SourceId, name);", body)
        self.assertIn('"{0} · level {1}", name, levels', body)

    def test_prerequisite_pages_translate_display_not_selection_identity(self):
        for name in ("CreationPrerequisitePage", "CreationPriorityCategoryPage"):
            source = (ROOT / f"src/Chummer.Android/Native/{name}.cs").read_text()
            self.assertIn('MagicCatalogStrings.OptionName("priority-rank",', source)
        detail = (ROOT / "src/Chummer.Android/Native/CreationPriorityDetailPage.cs").read_text()
        self.assertIn("MagicCatalogStrings.MetatypeName(option.MetatypeName)", detail)
        self.assertIn("MagicCatalogStrings.TalentName(option.Name)", detail)
        self.assertIn("SelectHeritageAsync(state, option.SelectionId)", detail)
        self.assertIn("SelectTalentAsync(state, option.SelectionId)", detail)


if __name__ == "__main__":
    unittest.main()
