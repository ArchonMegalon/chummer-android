import json
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
PROJECT = REPO / "src" / "Chummer.Android"
RESOURCES = PROJECT / "Resources" / "Localization"
PHONE_CAPABILITIES = (
    REPO / "docs/ANDROID_CHARACTER_SETTINGS_PHONE_CAPABILITIES.generated.json"
)


def load_resx(name: str) -> dict[str, str]:
    root = ET.parse(RESOURCES / name).getroot()
    return {
        entry.attrib["name"]: (entry.findtext("value") or "").strip()
        for entry in root.findall("data")
    }


class PhoneLocalizationSourceContractTests(unittest.TestCase):
    def test_english_german_and_spanish_catalogs_have_exact_nonempty_key_parity(self) -> None:
        catalogs = {
            "en": load_resx("PhoneStrings.resx"),
            "de": load_resx("PhoneStrings.de.resx"),
            "es": load_resx("PhoneStrings.es.resx"),
        }
        english_keys = set(catalogs["en"])
        self.assertGreaterEqual(len(english_keys), 40)
        for language, catalog in catalogs.items():
            self.assertEqual(english_keys, set(catalog), language)
            self.assertTrue(all(catalog.values()), language)
        self.assertEqual("Geschichten", catalogs["de"]["ShellStories"])
        self.assertEqual("Historias", catalogs["es"]["ShellStories"])

    def test_link_diagnostics_guidance_names_the_actual_destination_in_each_locale(self) -> None:
        for name in ("PhoneStrings.resx", "PhoneStrings.de.resx", "PhoneStrings.es.resx"):
            catalog = load_resx(name)
            destination = f"{catalog['ShellMore']} → {catalog['LinkedRecoveryTitle']}"
            self.assertIn(destination, catalog["LinkedRunnerOutcomeUnconfirmed"], name)
            self.assertIn(catalog["ShellRunners"], catalog["LinkedRecoveryEntryDetail"], name)

    def test_locale_policy_supports_regional_de_en_es_and_explicit_english_fallback(self) -> None:
        policy = (PROJECT / "Native" / "PhoneLocalePolicy.cs").read_text(encoding="utf-8")
        for language in ('"de"', '"en"', '"es"'):
            self.assertIn(language, policy)
        self.assertIn("EnglishLocale", policy)
        self.assertIn("UsesEnglishFallback", policy)
        # Only UI initialization leaves formatting alone; saved region selection
        # intentionally applies both current/default formatting on the next launch.
        ui_initialize = policy[policy.index("public static PhoneLocaleSelection InitializeFromSystemCulture"):]
        self.assertNotIn("CurrentCulture =", ui_initialize)
        self.assertNotIn("DefaultThreadCurrentCulture =", ui_initialize)
        self.assertIn("InitializeFromPreferences", policy)

    def test_ui_locale_is_initialized_before_content_and_page_composition(self) -> None:
        program = (PROJECT / "MauiProgram.cs").read_text(encoding="utf-8")
        initialize = program.index("PhoneLocalePolicy.InitializeFromPreferences(")
        materialize = program.index("AndroidBundledContentMaterializer.Materialize()")
        build = program.index("return builder.Build()")
        self.assertLess(initialize, materialize)
        self.assertLess(initialize, build)

    def test_first_level_phone_surfaces_use_resources_and_public_label_is_stories(self) -> None:
        shell = (PROJECT / "MainShell.cs").read_text(encoding="utf-8")
        home = (PROJECT / "Native" / "HomePage.cs").read_text(encoding="utf-8")
        more = (PROJECT / "Native" / "MorePage.cs").read_text(encoding="utf-8")
        runners = (PROJECT / "Native" / "PhoneShellPages.cs").read_text(encoding="utf-8")
        self.assertIn('PhoneStrings.Get("ShellStories", "Stories")', shell)
        self.assertNotIn('CreatePhoneTab<ShadowArchivePage>(services, "Archive"', shell)
        for source in (shell, home, more, runners):
            self.assertIn("PhoneStrings.Get", source)

    def test_stories_opens_the_private_native_book_not_the_unavailable_public_archive(self) -> None:
        shell = (PROJECT / "MainShell.cs").read_text(encoding="utf-8")
        program = (PROJECT / "MauiProgram.cs").read_text(encoding="utf-8")
        self.assertIn("CreatePhoneTab<PhoneStoriesPage>", shell)
        self.assertNotIn("CreatePhoneTab<ShadowArchivePage>", shell)
        self.assertIn("AddTransient<PhoneStoriesPage>()", program)
        for name in ("PhoneStrings.resx", "PhoneStrings.de.resx", "PhoneStrings.es.resx"):
            catalog = load_resx(name)
            for key in ("StoriesPrivateDetail", "StoriesChooseRunnerDetail", "StoriesChooseRunner"):
                self.assertTrue(catalog[key], (name, key))

    def test_settings_exposes_only_phone_meaningful_controls(self) -> None:
        settings = (PROJECT / "Native" / "ApplicationSettingsPage.cs").read_text(
            encoding="utf-8"
        )
        coordinator = (PROJECT / "Native" / "RunnerSessionCoordinator.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn('AutomationId = "settings-confirm-delete"', settings)
        self.assertIn('"settings-language"', settings)
        self.assertIn('"settings-region"', settings)
        self.assertIn("PhoneLocalePolicy.SavePreferences", settings)
        self.assertIn('AutomationId = "settings-updates-play-managed"', settings)
        self.assertIn('"settings-confirm-delete-experimental"', settings)
        self.assertIn("CurrentPhoneWizardScope.MarkExperimental(", settings)
        self.assertIn("if (_playReview is not null)", settings)
        self.assertIn("SaveDeleteConfirmationSettingAsync", settings)
        self.assertIn("ApplicationDeleteConfirmationMutation", coordinator)
        for desktop_only_id in (
            "settings-confirm-karma-expense",
            "settings-hide-master-index",
            "settings-hide-character-roster",
            "settings-search-in-category-only",
            "settings-allow-easter-eggs",
            "settings-prefer-nightly-builds",
            "settings-live-update-clean-character-files",
            "settings-custom-date-time-formats",
            "settings-date-format",
            "settings-time-format",
            "settings-dates-include-time",
        ):
            self.assertNotIn(f'AutomationId = "{desktop_only_id}"', settings)
        self.assertNotIn("SaveApplicationSettingsAsync", coordinator)

    def test_creation_dashboard_resources_preserve_placeholders_and_no_duplicate_keys(self) -> None:
        import re
        catalogs = [load_resx(f"PhoneStrings{suffix}.resx") for suffix in ("", ".de", ".es")]
        for suffix in ("", ".de", ".es"):
            names = [row.attrib["name"] for row in ET.parse(RESOURCES / f"PhoneStrings{suffix}.resx").getroot().findall("data")]
            self.assertEqual(len(names), len(set(names)), suffix)
        for key, english in catalogs[0].items():
            if not key.startswith(("Creation", "Runner")):
                continue
            placeholders = re.findall(r"\{\d+(?::[^}]+)?\}", english)
            for catalog in catalogs[1:]:
                self.assertEqual(placeholders, re.findall(r"\{\d+(?::[^}]+)?\}", catalog[key]), key)
        self.assertEqual("Bauart · {0}", catalogs[1]["CreationMethodTitle"])
        self.assertEqual("Vor- und Nachteile", catalogs[1]["CreationStep.qualities"])

    def test_typed_budget_labels_keep_canonical_snapshot_display_identity(self) -> None:
        source = (PROJECT / "Native" / "BuildPage.cs").read_text(encoding="utf-8")
        self.assertIn("BuildPageUiProjection.BudgetLabel(budget, projectedBudget.BudgetId)", source)
        self.assertIn('CreationNavigationRow($"{budgetLabel} · {amount}"', source)
        self.assertIn("DisplayAlertAsync(budgetLabel,", source)

    def test_attribute_editor_and_review_localize_budgets_without_rewriting_ledgers(self) -> None:
        source = (PROJECT / "Native" / "CreationAttributesPage.cs").read_text(encoding="utf-8")
        for canonical in ("NormalAttributes", "SpecialAttributes", "Karma"):
            self.assertGreaterEqual(source.count("CharacterCreationBudgetIds." + canonical), 2)
        self.assertIn("BuildPageUiProjection.BudgetLabel(budget, canonicalBudgetId)", source)
        self.assertNotIn("budget.Label", source)
        self.assertIn('SemanticProperties.SetDescription(jump, label)', source)
        self.assertIn('PhoneStrings.Get("CreationUnit." + unit, unit)', source)

    def test_skills_editor_and_review_localize_display_without_changing_typed_ids(self) -> None:
        source = (PROJECT / "Native" / "CreationSkillsPage.cs").read_text(encoding="utf-8")
        for canonical in ("ActiveSkills", "SkillGroups", "KnowledgeSkills"):
            self.assertEqual(2, source.count("CharacterCreationBudgetIds." + canonical))
        self.assertEqual(2, source.count("BuildPageUiProjection.BudgetLabel(budget, canonicalBudgetId)"))
        self.assertNotIn("budget.Label", source)
        self.assertIn("CreationAllocationStrings.AttributeName(source.DefaultAttribute)", source)
        self.assertIn('Token(budget.BudgetId)', source)
        self.assertIn('budget.Remaining.ToString("0.##", CultureInfo.CurrentCulture)', source)
        self.assertNotIn('budget.Remaining.ToString("0.##", CultureInfo.InvariantCulture)', source)
        for field in ("ActivePointsRemaining", "SkillGroupPointsRemaining", "KnowledgePointsRemaining"):
            self.assertIn(f"receipt.{field}.ToString(CultureInfo.CurrentCulture)", source)

    def test_character_settings_scope_is_explicit_in_all_supported_languages(self) -> None:
        catalogs = {
            "en": load_resx("PhoneStrings.resx"),
            "de": load_resx("PhoneStrings.de.resx"),
            "es": load_resx("PhoneStrings.es.resx"),
        }
        scope_keys = {
            "CharacterSettingsCustomDataScope",
            "CharacterSettingsRulesScope",
            "CharacterSettingsUnsupportedScope",
            "CharacterSettingsPhoneMessage",
            "CharacterSettingsTitle",
            "CharacterSettingsActionSave",
            "CharacterSettingsActionSaveAndClose",
            "CharacterSettingsActionSaveAs",
            "CharacterSettingsActionRename",
            "CharacterSettingsActionDelete",
            "CharacterSettingsActionRestoreDefaults",
            "CharacterSettingsActionCancel",
            "CharacterSettingsProfile",
            "CharacterSettingsProfileName",
            "CharacterSettingsSection",
            "CharacterSettingsSectionWare",
            "CharacterSettingsSectionRules",
            "CharacterSettingsSectionKarma",
            "CharacterSettingsSectionLimits",
            "CharacterSettingsSectionBuild",
        }
        inventory = json.loads(PHONE_CAPABILITIES.read_text(encoding="utf-8"))
        visible = [
            row
            for row in inventory["controls"]
            if row["phoneStatus"] == "visible_editable"
        ]
        capability_label_keys = {row["labelResourceKey"] for row in visible}
        for language, catalog in catalogs.items():
            required = scope_keys | capability_label_keys
            self.assertTrue(required.issubset(catalog), language)
            self.assertTrue(all(catalog[key] for key in required), language)

        for row in visible:
            self.assertEqual(
                row["englishLabel"],
                catalogs["en"][row["labelResourceKey"]],
                row["legacyControl"],
            )

        self.assertIn("desktop-only", catalogs["en"]["CharacterSettingsCustomDataScope"])
        self.assertIn("nur auf dem Desktop", catalogs["de"]["CharacterSettingsCustomDataScope"])
        self.assertIn("exclusivos del escritorio", catalogs["es"]["CharacterSettingsCustomDataScope"])
        self.assertEqual("Character Settings", catalogs["en"]["CharacterSettingsTitle"])
        self.assertEqual("Charaktereinstellungen", catalogs["de"]["CharacterSettingsTitle"])
        self.assertEqual("Ajustes del personaje", catalogs["es"]["CharacterSettingsTitle"])
        self.assertEqual(
            ["Save", "Save & Close", "Save As", "Rename", "Delete", "Restore Defaults", "Cancel"],
            [catalogs["en"][key] for key in (
                "CharacterSettingsActionSave",
                "CharacterSettingsActionSaveAndClose",
                "CharacterSettingsActionSaveAs",
                "CharacterSettingsActionRename",
                "CharacterSettingsActionDelete",
                "CharacterSettingsActionRestoreDefaults",
                "CharacterSettingsActionCancel",
            )],
        )


if __name__ == "__main__":
    unittest.main()
