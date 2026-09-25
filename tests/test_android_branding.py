"""Startup and launcher must use the same owner-selected troll artwork."""
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


class AndroidBrandingTests(unittest.TestCase):
    def test_splash_uses_launcher_foreground_not_retired_s_mark(self):
        app = Path(__file__).resolve().parents[1] / "src/Chummer.Android"
        project = ET.parse(app / "Chummer.Android.csproj").getroot()
        icons = project.findall(".//MauiIcon")
        splashes = project.findall(".//MauiSplashScreen")
        self.assertEqual(len(icons), 1)
        self.assertEqual(len(splashes), 1)
        # Resizetizer cannot use the same resource for both icon and splash.
        splash = app / splashes[0].get("Include")
        foreground = app / icons[0].get("ForegroundFile")
        self.assertNotEqual(splash.stem, foreground.stem)
        self.assertEqual(splash.read_bytes(), foreground.read_bytes())
        self.assertEqual(splashes[0].get("Color"), icons[0].get("Color"))
        artwork = ET.parse(splash).getroot()
        self.assertEqual(artwork.tag, "{http://www.w3.org/2000/svg}svg")
        self.assertEqual(artwork.get("viewBox"), "0 0 2048 2048")


if __name__ == "__main__":
    unittest.main()
