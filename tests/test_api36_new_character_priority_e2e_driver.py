from __future__ import annotations

import ast
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_new_character_priority_e2e.py"


class Api36NewCharacterPriorityE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_and_syntax_valid(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertIn('"profile": "phone"', source)
        self.assertNotIn('"profile": "tablet"', source)
        self.assertNotIn("tablet-", source)

    def test_driver_edits_every_proven_priority_control(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        for marker in (
            "dialog-field-newcharactermetatypecategory",
            "dialog-field-newcharactermetatype",
            "dialog-field-newcharactermetavariant",
            "dialog-field-newcharacterpriorityheritage",
            "dialog-field-newcharacterpriorityattributes",
            "dialog-field-newcharacterprioritytalent",
            "dialog-field-newcharacterpriorityskills",
            "dialog-field-newcharacterpriorityresources",
            "dialog-field-newcharacterprioritytalentchoice",
            "dialog-field-newcharacterpriorityskillchoice1",
            "dialog-field-newcharacterpriorityskillchoice2",
            "dialog-field-newcharacterpriorityskillchoice3",
            "dialog-field-newcharacterforce",
            "dialog-field-newcharacterpossessionbased",
            "dialog-field-newcharacterpossessionmethod",
            "dialog-action-complete-new-character-workflow",
        ):
            self.assertIn(marker, source)

    def test_driver_proves_exact_xml_and_restart_persistence(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        for marker in (
            '"prioritymetatype": "A,4"',
            '"priorityattributes": "C,2"',
            '"priorityspecial": "B,3"',
            '"priorityskills": "D,1"',
            '"priorityresources": "E,0"',
            '"prioritytalent": "Mystic Adept"',
            'EXPECTED_PRIORITY_SKILLS = ("Summoning", "Binding", "Gymnastics")',
            '"force": "6"',
            '"possessionmethod": "Inhabitation"',
            '== "30918b00-6dae-4989-9b6e-219c4bd6ac7e"',
            'device.shell("am", "force-stop"',
            '"workspacePriorityPersisted": "pass"',
            '"processRestartPriorityPersistence": "pass"',
            '"workspaceSpiritPossessionPersisted": "pass"',
            '"processRestartSpiritPossessionPersistence": "pass"',
        ):
            self.assertIn(marker, source)

    def test_driver_seals_sources_and_uses_robust_install(self) -> None:
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
