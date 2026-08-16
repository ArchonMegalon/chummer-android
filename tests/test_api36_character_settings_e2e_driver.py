from __future__ import annotations

import ast
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_character_settings_e2e.py"


class Api36CharacterSettingsE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_and_syntax_valid(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertIn('"profile": "phone"', source)
        self.assertNotIn('"profile": "tablet"', source)

    def test_driver_reaches_every_phone_section_and_field_kind(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        for marker in (
            "Ware, armor, and vehicles",
            "Sourcebooks",
            "Rules and options",
            "Formulas and formatting",
            "Karma costs",
            "Custom data",
            "Limits and initiative",
            "Build method",
            "command-action-character-settings",
            "dialog-action-save-and-close",
        ):
            self.assertIn(marker, source)

    def test_driver_proves_exact_catalog_and_restart_readback(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        for marker in (
            "read_catalog",
            "assert_catalog_xml",
            "processRestartCatalogPersistence",
            "processRestartUiReadback",
            "characterSettingsContractSha256",
        ):
            self.assertIn(marker, source)


if __name__ == "__main__":
    unittest.main()
