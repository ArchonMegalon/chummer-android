from __future__ import annotations

import ast
import importlib.util
import sys
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_character_settings_actions_e2e.py"
SETTINGS_DRIVER = REPO / "tests" / "run_api36_character_settings_e2e.py"
SHARED_DRIVER = REPO / "tests" / "run_api36_editing_e2e.py"
SHARED_SPEC = importlib.util.spec_from_file_location("run_api36_editing_e2e", SHARED_DRIVER)
assert SHARED_SPEC is not None and SHARED_SPEC.loader is not None
shared = importlib.util.module_from_spec(SHARED_SPEC)
sys.modules[SHARED_SPEC.name] = shared
SHARED_SPEC.loader.exec_module(shared)
SETTINGS_SPEC = importlib.util.spec_from_file_location(
    "run_api36_character_settings_e2e",
    SETTINGS_DRIVER,
)
assert SETTINGS_SPEC is not None and SETTINGS_SPEC.loader is not None
settings = importlib.util.module_from_spec(SETTINGS_SPEC)
sys.modules[SETTINGS_SPEC.name] = settings
SETTINGS_SPEC.loader.exec_module(settings)
DRIVER_SPEC = importlib.util.spec_from_file_location(
    "run_api36_character_settings_actions_e2e",
    DRIVER,
)
assert DRIVER_SPEC is not None and DRIVER_SPEC.loader is not None
driver = importlib.util.module_from_spec(DRIVER_SPEC)
sys.modules[DRIVER_SPEC.name] = driver
DRIVER_SPEC.loader.exec_module(driver)


class Api36CharacterSettingsActionsE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_and_syntax_valid(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertIn('"profile": "phone"', source)
        self.assertNotIn('"profile": "tablet"', source)

    def test_driver_covers_exactly_the_five_phone_profile_actions(self) -> None:
        self.assertEqual(
            {
                "cboSetting",
                "cmdSaveAs",
                "cmdRestoreDefaults",
                "cmdDelete",
                "cmdRename",
            },
            set(driver.CONTROLS),
        )

    def test_driver_proves_each_action_and_restart_readback(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        for marker in (
            'tap_action(device, "save-as")',
            'tap_action(device, "rename")',
            'tap_action(device, "restore-defaults")',
            'tap_action(device, "delete")',
            "assert_after_restart",
            '"processRestartUiReadback": "pass"',
        ):
            self.assertIn(marker, source)

    def test_receipt_binds_both_drivers_and_complete_settings_source_graph(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        for marker in (
            "characterSettingsDriverSha256",
            "sharedDriverSha256",
            "nativeCommandPageSha256",
            "nativeDialogPageSha256",
            "characterSettingsDialogSha256",
            "characterSettingsProfilesSha256",
            "characterSettingsContractSha256",
            "dialogCoordinatorSha256",
        ):
            self.assertIn(marker, source)


if __name__ == "__main__":
    unittest.main()
