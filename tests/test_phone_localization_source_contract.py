import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
PROJECT = REPO / "src" / "Chummer.Android"
RESOURCES = PROJECT / "Resources" / "Localization"


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

    def test_locale_policy_supports_regional_de_en_es_and_explicit_english_fallback(self) -> None:
        policy = (PROJECT / "Native" / "PhoneLocalePolicy.cs").read_text(encoding="utf-8")
        for language in ('"de"', '"en"', '"es"'):
            self.assertIn(language, policy)
        self.assertIn("EnglishLocale", policy)
        self.assertIn("UsesEnglishFallback", policy)
        self.assertNotIn("CurrentCulture =", policy)
        self.assertNotIn("DefaultThreadCurrentCulture =", policy)

    def test_ui_locale_is_initialized_before_content_and_page_composition(self) -> None:
        program = (PROJECT / "MauiProgram.cs").read_text(encoding="utf-8")
        initialize = program.index("PhoneLocalePolicy.InitializeFromSystemCulture()")
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


if __name__ == "__main__":
    unittest.main()
