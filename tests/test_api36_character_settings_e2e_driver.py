from __future__ import annotations

import ast
import importlib.util
import inspect
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_character_settings_e2e.py"
SHARED_DRIVER = REPO / "tests" / "run_api36_editing_e2e.py"
SHARED_SPEC = importlib.util.spec_from_file_location("run_api36_editing_e2e", SHARED_DRIVER)
assert SHARED_SPEC is not None and SHARED_SPEC.loader is not None
shared = importlib.util.module_from_spec(SHARED_SPEC)
sys.modules[SHARED_SPEC.name] = shared
SHARED_SPEC.loader.exec_module(shared)
DRIVER_SPEC = importlib.util.spec_from_file_location(
    "run_api36_character_settings_e2e",
    DRIVER,
)
assert DRIVER_SPEC is not None and DRIVER_SPEC.loader is not None
driver = importlib.util.module_from_spec(DRIVER_SPEC)
sys.modules[DRIVER_SPEC.name] = driver
DRIVER_SPEC.loader.exec_module(driver)


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
            "load_value_controls",
            "discover_section_controls",
            "edit_all_value_controls",
            "wait_exact_field",
            "set_exact_text",
            "len(controls) != 150",
        ):
            self.assertIn(marker, source)

    def test_driver_proves_exact_catalog_and_restart_readback(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        for marker in (
            "read_catalog",
            "assert_catalog_xml",
            "processRestartCatalogPersistence",
            "processRestartUiReadback",
            "profileSavedWithoutClosing",
            "assert_all_controls_persisted",
            "assert_all_ui_readback",
            '"controls": control_proofs',
            '"allValueControlsEdited": "pass"',
            '"allValueControlsCatalogPersisted": "pass"',
            '"allValueControlsRestartUiReadback": "pass"',
            "characterSettingsContractSha256",
        ):
            self.assertIn(marker, source)

    def test_character_settings_hierarchy_reader_recovers_without_changing_shared_driver(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertIn("class CharacterSettingsDevice(shared.Device):", source)
        self.assertIn("timeout = min(timeout, 30)", source)
        self.assertNotIn(
            "except (subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:",
            inspect.getsource(shared.Device.hierarchy),
        )
        with tempfile.TemporaryDirectory() as temporary:
            device = driver.CharacterSettingsDevice(
                Path("/unused/adb"),
                "emulator-test",
                Path(temporary),
            )
            timeout = subprocess.TimeoutExpired(
                ["adb", "shell", "uiautomator"],
                30,
                output=b"partial hierarchy output",
            )
            with patch.object(
                shared.Device,
                "hierarchy",
                side_effect=timeout,
            ) as hierarchy:
                self.assertEqual([], device.hierarchy(deadline=41.0))
            hierarchy.assert_called_once_with(deadline=41.0)
            self.assertIn(
                "partial hierarchy output",
                (Path(temporary) / "last-invalid-hierarchy.txt").read_text(encoding="utf-8"),
            )


if __name__ == "__main__":
    unittest.main()
