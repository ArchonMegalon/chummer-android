import importlib.util
import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
SCRIPT = REPO / "scripts" / "materialize_chummer5_character_settings_contract.py"
OUTPUT = REPO / "docs" / "CHUMMER5_CHARACTER_SETTINGS_CONTRACT.generated.json"

SPEC = importlib.util.spec_from_file_location("character_settings_contract", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
contract = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(contract)


class Chummer5CharacterSettingsContractTests(unittest.TestCase):
    def test_generated_contract_is_current(self) -> None:
        subprocess.run(
            [sys.executable, str(SCRIPT), "--check"],
            cwd=REPO,
            check=True,
            env={**os.environ, "PYTHONDONTWRITEBYTECODE": "1"},
        )

    def test_every_edit_character_settings_control_resolves_fail_closed(self) -> None:
        payload = json.loads(OUTPUT.read_text(encoding="utf-8"))
        self.assertEqual("complete", payload["status"])
        self.assertFalse(payload["implementationEvidence"])
        self.assertEqual(162, payload["summary"]["controlCount"])
        self.assertEqual(162, payload["summary"]["resolvedControlCount"])
        self.assertEqual(0, payload["summary"]["unresolvedControlCount"])
        self.assertEqual([], payload["unresolvedControls"])
        gate = payload["phoneBetaWizardGate"]
        self.assertEqual("sr5_wizards_only", gate["proofScope"])
        self.assertEqual(3, gate["requiredJourneyCount"])
        self.assertEqual(
            [
                "creation-prerequisite",
                "career-active-skill-advance",
                "career-weapon-fire",
            ],
            gate["requiredJourneys"],
        )
        self.assertFalse(gate["settingsContractRequiredJourney"])
        self.assertFalse(gate["publicationAuthorized"])
        self.assertTrue(
            all(row["persistencePaths"] for row in payload["controls"])
        )

    def test_contract_preserves_nested_and_collection_semantics(self) -> None:
        payload = json.loads(OUTPUT.read_text(encoding="utf-8"))
        rows = {row["legacyControl"]: row for row in payload["controls"]}
        self.assertIn(
            "settings/karmacost/karmaattribute",
            rows["nudKarmaAttribute"]["persistencePaths"],
        )
        self.assertEqual(
            ["settings/books/book"], rows["treSourcebook"]["persistencePaths"]
        )
        self.assertEqual(
            ["settings/bannedwaregrades/grade"],
            rows["chkGrade"]["persistencePaths"],
        )
        self.assertEqual(
            "move_custom_data_directory_to_top",
            rows["cmdToTopCustomDirectoryLoadOrder"]["semanticOperation"],
        )
        self.assertEqual("save_profile_as", rows["cmdSaveAs"]["semanticOperation"])
        self.assertEqual(
            ["settings/exceednegativequalities"],
            rows["chkExceedNegativeQualities"]["persistencePaths"],
        )
        self.assertEqual(
            ["settings/mysadeptsecondmagattribute"],
            rows["chkMysAdeptSecondMAGAttribute"]["persistencePaths"],
        )
        self.assertEqual(
            ["settings/doencumbrancepenaltywoundmodifier"],
            rows["chkEncumbrancePenaltyWoundModifier"]["persistencePaths"],
        )

    def test_source_input_labels_are_clone_portable(self) -> None:
        payload = json.loads(OUTPUT.read_text(encoding="utf-8"))
        self.assertEqual(
            [
                "chummer5a/Chummer/Forms/EditCharacterSettings.cs",
                "chummer5a/Chummer/Backend/Character Settings/CharacterSettings.cs",
                "chummer5a/Chummer/data/settings.xml",
                "docs/ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json",
                "eng/api36-sr5-wizard-gate-authority.json",
            ],
            [row["path"] for row in payload["sourceInputs"]],
        )
        self.assertTrue(
            all(not Path(row["path"]).is_absolute() for row in payload["sourceInputs"])
        )

    def test_check_rejects_a_stale_output(self) -> None:
        payload = json.loads(OUTPUT.read_text(encoding="utf-8"))
        payload["summary"]["controlCount"] = 0
        with tempfile.TemporaryDirectory(dir=REPO / "docs") as temporary:
            stale = Path(temporary) / "contract.json"
            stale.write_text(json.dumps(payload), encoding="utf-8")
            result = subprocess.run(
                [sys.executable, str(SCRIPT), "--check", "--output", str(stale)],
                cwd=REPO,
                check=False,
                capture_output=True,
                text=True,
                env={**os.environ, "PYTHONDONTWRITEBYTECODE": "1"},
            )
        self.assertEqual(1, result.returncode)
        self.assertIn("stale", result.stderr)


if __name__ == "__main__":
    unittest.main()
