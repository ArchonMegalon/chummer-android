from pathlib import Path
import unittest
import uuid
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
RESOURCE = ROOT / "src/Chummer.Android/Resources/Localization"


class QualityCatalogStringsTests(unittest.TestCase):
    def test_every_pinned_quality_has_an_unambiguous_german_label(self):
        keys = None
        for suffix in ("", ".de"):
            rows = ET.parse(RESOURCE / f"QualityCatalogStrings{suffix}.resx").getroot().findall("data")
            names = [row.get("name") for row in rows]
            self.assertEqual(len(rows), 803)
            self.assertEqual(len(rows), len(set(names)))
            self.assertTrue(all(0 < len(row.findtext("value")) < 200 for row in rows))
            for name in names:
                self.assertEqual(name, f"Quality.{uuid.UUID(name.removeprefix('Quality.').removesuffix('.Name'))}.Name")
            if keys is not None:
                self.assertEqual(keys, set(names))
            keys = set(names)

    def test_renamed_legacy_row_uses_reviewed_label(self):
        labels = {r.get("name"): r.findtext("value") for r in ET.parse(RESOURCE / "QualityCatalogStrings.de.resx").getroot().findall("data")}
        self.assertEqual(labels["Quality.83e5f9a2-e76b-4a11-81db-4b0466b727ce.Name"], "Erst schießen, keine Fragen")

    def test_display_paths_use_source_identity_without_replacing_selection_ids(self):
        page = (ROOT / "src/Chummer.Android/Native/CreationQualitiesPage.cs").read_text()
        for variable in ("grant", "option", "_option", "selection"):
            self.assertIn(f"QualityCatalogStrings.Name({variable}.SourceId, {variable}.Name)", page)
        self.assertIn("QualityCatalogStrings.MatchesSearch(", page)
        self.assertIn("_draft.WithToggle(_option)", page)
        self.assertIn(".ThenBy(option => option.OptionId, StringComparer.Ordinal)", page)
        self.assertIn("CreationQualityInfo.Effects(_sourceXml, _rating)", page)


if __name__ == "__main__":
    unittest.main()
