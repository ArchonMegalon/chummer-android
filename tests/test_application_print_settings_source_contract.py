import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
PROJECT = REPO / "src" / "Chummer.Android"


def catalog(file_name: str) -> dict[str, str]:
    root = ET.parse(PROJECT / "Resources" / "Localization" / file_name).getroot()
    return {
        item.attrib["name"]: (item.findtext("value") or "").strip()
        for item in root.findall("data")
    }


class ApplicationPrintSettingsSourceContractTests(unittest.TestCase):
    def test_phone_has_deep_navigation_and_six_stable_controls(self) -> None:
        parent = (PROJECT / "Native" / "ApplicationSettingsPage.cs").read_text(encoding="utf-8")
        page = (PROJECT / "Native" / "ApplicationPrintSettingsPage.cs").read_text(encoding="utf-8")

        self.assertIn("new ApplicationPrintSettingsPage(Coordinator)", parent)
        self.assertIn('automationId: "settings-open-print-settings"', parent)
        for automation_id in (
            "settings-print-to-file-first",
            "settings-print-zero-rating-skills",
            "settings-print-expenses",
            "settings-print-free-expenses",
            "settings-print-notes",
            "settings-insert-pdf-notes",
        ):
            self.assertIn(f'"{automation_id}"', page)
        self.assertIn("SaveApplicationPrintSettingsAsync", page)
        self.assertIn("_baseline.Revision", page)
        self.assertIn("Navigation.PopToRootAsync()", page)

    def test_coordinator_submits_one_typed_revision_checked_snapshot(self) -> None:
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        method = coordinator[coordinator.index("SaveApplicationPrintSettingsAsync") :]
        method = method[: method.index("private CharacterRosterDocumentIdentity")]

        self.assertEqual(1, method.count("ApplyPrintSettingsSnapshot"))
        self.assertIn("expectedRevision", method)
        for identity in (
            "PrintToFileFirst",
            "PrintSkillsWithZeroRating",
            "PrintExpenses",
            "PrintFreeExpenses",
            "PrintNotes",
            "InsertPdfNotesIfAvailable",
        ):
            self.assertIn(f"ApplicationSettingIdentity.{identity}", method)

    def test_free_expenses_is_disabled_and_cleared_with_expenses(self) -> None:
        page = (PROJECT / "Native" / "ApplicationPrintSettingsPage.cs").read_text(encoding="utf-8")
        dependency = page[page.index("private void UpdateExpenseDependency") :]
        self.assertIn("_printFreeExpenses.IsEnabled = _printExpenses.IsToggled", dependency)
        self.assertIn("_printFreeExpenses.IsToggled = false", dependency)

    def test_new_copy_is_complete_in_english_german_and_spanish(self) -> None:
        catalogs = {
            "en": catalog("PhoneStrings.resx"),
            "de": catalog("PhoneStrings.de.resx"),
            "es": catalog("PhoneStrings.es.resx"),
        }
        keys = {
            "ApplicationPrintSettingsTitle",
            "ApplicationPrintSettingsEyebrow",
            "ApplicationPrintSettingsSummary",
            "ApplicationPrintSettingsNavigationDetail",
            "ApplicationPrintToFileFirstTitle",
            "ApplicationPrintToFileFirstDescription",
            "ApplicationPrintZeroSkillsTitle",
            "ApplicationPrintZeroSkillsDescription",
            "ApplicationPrintExpensesTitle",
            "ApplicationPrintExpensesDescription",
            "ApplicationPrintFreeExpensesTitle",
            "ApplicationPrintFreeExpensesDescription",
            "ApplicationPrintNotesTitle",
            "ApplicationPrintNotesDescription",
            "ApplicationInsertPdfNotesTitle",
            "ApplicationInsertPdfNotesDescription",
            "ApplicationSettingsRevision",
            "ApplicationPrintSettingsSaved",
        }
        for language, values in catalogs.items():
            self.assertTrue(keys.issubset(values), language)
            self.assertTrue(all(values[key] for key in keys), language)
        self.assertEqual("Druck & PDF-Notizen", catalogs["de"]["ApplicationPrintSettingsTitle"])
        self.assertEqual("Impresión y notas PDF", catalogs["es"]["ApplicationPrintSettingsTitle"])


if __name__ == "__main__":
    unittest.main()
