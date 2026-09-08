"""Structural guards only; actual behavior is in SkillsReReviewNativeRuntimeTests."""
from pathlib import Path
import re
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
NATIVE = ROOT / "src/Chummer.Android/Native"
RESOURCES = ROOT / "src/Chummer.Android/Resources/Localization"


class SkillsReReviewSourceTests(unittest.TestCase):
    def test_all_copy_and_placeholders_have_three_actual_resource_packs(self):
        catalogs = []
        for locale in ("", ".de", ".es"):
            entries = ET.parse(RESOURCES / f"CreationAllocationStrings{locale}.resx").getroot().findall("data")
            values = {row.attrib["name"]: row.findtext("value", "") for row in entries}
            self.assertEqual(len(entries), len(values), "Duplicate resource key")
            catalogs.append({key: value for key, value in values.items() if key.startswith("SkillsReReview.")})
        self.assertEqual(len(catalogs[0]), 30)
        for catalog in catalogs:
            self.assertEqual(set(catalog), set(catalogs[0]))
            for key, value in catalog.items():
                self.assertTrue(value.strip(), key)
                self.assertEqual(re.findall(r"\{\d+\}", value), re.findall(r"\{\d+\}", catalogs[0][key]), key)
        page = (NATIVE / "CreationSkillsReReviewPage.cs").read_text()
        references = set(re.findall(r'(?:Text|Format)\("([A-Za-z]+)"', page))
        self.assertTrue({"SkillsReReview." + key for key in references} <= set(catalogs[0]))

    def test_page_does_not_mutate_or_resolve_rules_and_fences_departure(self):
        page = (NATIVE / "CreationSkillsReReviewPage.cs").read_text()
        self.assertIn("await Task.Run(() => Coordinator.PreviewCreationSkillsReReview", page)
        self.assertIn("Coordinator.ConfirmCreationSkillsReReviewAsync", page)
        self.assertIn("generation != Volatile.Read(ref _generation)", page)
        self.assertIn("_body.IsEnabled = false", page)
        self.assertIn("if (_confirmation?.Receipt is { } receipt)", page)
        self.assertIn("preview.Changes", page)
        self.assertIn("change.SourceAnchorIds", page)
        for forbidden in ("File.Write", "XDocument", "CanEdit = true", "FileWorkspaceStore", "EvaluateAllocations"):
            self.assertNotIn(forbidden, page)

    def test_coordinator_reprojects_before_commit_and_preserves_post_commit_receipt(self):
        source = (NATIVE / "RunnerSessionCoordinator.SkillsReReview.cs").read_text()
        confirm = source[source.index("private CreationSkillsPhoneConfirmResult ConfirmSkillsReReviewCore"):]
        self.assertLess(confirm.index("service.PreviewReReview"), confirm.index("service.ConfirmReReview"))
        self.assertLess(confirm.index("Equal(preview, canonical)"), confirm.index("service.ConfirmReReview"))
        self.assertIn("if (!explicitlyReviewed)", confirm)
        self.assertIn("PostCommitRefreshRequired", confirm)
        self.assertIn("WithWorkspaceActivationGateAsync", source)
        self.assertIn("await Task.Run(() => ConfirmSkillsReReviewCore", source)
        self.assertNotIn("with { CanEdit = true", source)


if __name__ == "__main__":
    unittest.main()
