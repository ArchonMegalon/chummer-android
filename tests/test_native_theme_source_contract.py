import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
NATIVE = ROOT / "src" / "Chummer.Android" / "Native"


class NativeThemeSourceContractTests(unittest.TestCase):
    def test_modal_wizard_navigation_has_explicit_light_surface_and_dark_text(self) -> None:
        app = ET.parse(ROOT / "src/Chummer.Android/App.xaml").getroot()
        ns = {"m": "http://schemas.microsoft.com/dotnet/2021/maui"}
        key = "{http://schemas.microsoft.com/winfx/2009/xaml}Key"
        colours = {node.get(key): node.text for node in app.findall(".//m:Color", ns)}
        styles = app.findall(".//m:Style[@TargetType='NavigationPage']", ns)
        self.assertEqual(1, len(styles))
        setters = {node.get("Property"): node.get("Value") for node in styles[0]}
        self.assertEqual("{StaticResource ChummerSurface}", setters["BarBackgroundColor"])
        self.assertEqual("{StaticResource ChummerInk}", setters["BarTextColor"])
        self.assertEqual("#FFFFFF", colours["ChummerSurface"])
        self.assertEqual("#102426", colours["ChummerInk"])

    def test_launcher_assigns_generated_troll_assets(self) -> None:
        manifest = ET.parse(ROOT / "src/Chummer.Android/Platforms/Android/AndroidManifest.xml").getroot()
        app = manifest.find("application")
        ns = "{http://schemas.android.com/apk/res/android}"
        self.assertIsNotNone(app)
        self.assertEqual("@mipmap/appicon", app.get(ns + "icon"))
        self.assertEqual("@mipmap/appicon_round", app.get(ns + "roundIcon"))

    def test_life_fields_and_book_have_explicit_readable_styles(self) -> None:
        completion = (NATIVE / "LifeModuleCompletionPage.cs").read_text()
        self.assertIn("NativeTheme.TextField(id, value)", completion)
        self.assertIn("TextColor = NativeTheme.Text, PlaceholderColor = NativeTheme.Muted", completion)
        self.assertIn('"life-starting-dice-help"', completion)
        self.assertIn("SemanticProperties.SetDescription(acknowledgement", completion)
        for name in ("RetainedOriginBookPage.cs", "OriginBookProseReviewPage.cs", "OriginDossierPage.cs"):
            self.assertIn("NativeTheme.BookProse(", (NATIVE / name).read_text())
        build = (NATIVE / "BuildPage.cs").read_text()
        route = build[build.index("private async Task OpenSr5LifeModuleOriginAsync"):]
        route = route[:route.index("private Task OpenCreationPrerequisiteAsync")]
        self.assertIn("new RetainedOriginBookPage(Coordinator)", route)
        self.assertNotIn("new OriginDossierBookPage", route)

    def test_dice_help_is_localized_and_explains_sum_not_hits(self) -> None:
        for suffix, phrase in (("", "not hits"), (".de", "nicht die Erfolge"), (".es", "no los éxitos")):
            root = ET.parse(ROOT / f"src/Chummer.Android/Resources/Localization/CreationAllocationStrings{suffix}.resx").getroot()
            help_text = root.find("data[@name='Karma.DiceTotalHelp']/value").text
            self.assertIn(phrase, help_text)
            self.assertIn("7", help_text)

    def test_downtime_success_state_uses_a_real_shared_semantic_color(self) -> None:
        theme = (NATIVE / "NativeTheme.cs").read_text(encoding="utf-8")
        downtime = (NATIVE / "Sr5DowntimeCalendarWizardPage.cs").read_text(
            encoding="utf-8"
        )
        stubs = (
            ROOT
            / "tests"
            / "Chummer.Android.Sr5DowntimeCalendar.NativeCompile.Tests"
            / "NativeCompileStubs.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("public static readonly Color Success", theme)
        self.assertIn("TextColor = Success", theme)
        self.assertIn("NativeTheme.Success", downtime)
        self.assertIn("Color Success", stubs)


if __name__ == "__main__":
    unittest.main()
