from __future__ import annotations

import ast
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_new_character_settings_e2e.py"


class Api36NewCharacterSettingsE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_and_syntax_valid(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertIn('"profile": "phone"', source)
        self.assertNotIn('"profile": "tablet"', source)
        self.assertNotIn("tablet-", source)

    def test_driver_exercises_real_build_setting_controls_and_commit(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        for marker in (
            "dialog-field-newcharactersetting",
            "dialog-field-newcharacterignorerules",
            "dialog-action-create-character",
            "dialog-action-complete-new-character-workflow",
            "Character Setting",
            "Ignore Character Creation Rules",
        ):
            self.assertIn(marker, source)

    def test_driver_proves_workspace_and_restart_persistence(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        for marker in (
            "workspace_payloads",
            "assert_persisted_build_settings",
            'character.findtext("settings"',
            'character.findtext("ignorerules"',
            'device.shell("am", "force-stop"',
            '"workspaceBuildSettingsPersisted": "pass"',
            '"processRestartBuildSettingsPersistence": "pass"',
        ):
            self.assertIn(marker, source)

    def test_driver_seals_implementation_sources_and_uses_robust_install(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        for marker in (
            '"--no-streaming"',
            'configured_workspace_root = os.environ.get("CHUMMER_COMPLETE_ROOT")',
            '/ "chummer-presentation"',
            '"dialogFactorySha256"',
            '"dialogCoordinatorSha256"',
            '"nativeDialogPageSha256"',
            '"buildPageSha256"',
        ):
            self.assertIn(marker, source)


if __name__ == "__main__":
    unittest.main()
