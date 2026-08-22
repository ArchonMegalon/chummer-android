import ast
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests/run_api36_roster_sort_e2e.py"
FIXTURE = REPO / "tests/fixtures/roster-sort-e2e.chum5"
INVENTORY_SCRIPT = REPO / "scripts/materialize_chummer5_editability_inventory.py"
PRESENTATION = REPO.parent / "presentation"
CORE = REPO.parent / "core"


class Api36RosterSortDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_api36_arm64_package_and_digest_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertIn('CONTROL = "tsSort"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('abi != "arm64-v8a"', source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "roster-sort"', source)
        self.assertIn('device.shell("pm", "path", shared.PACKAGE)', source)
        self.assertIn('"rosterFavoriteStoreSha256"', source)
        self.assertIn('"runnerFixtureSha256"', source)
        self.assertIn("base64.b64encode", source)
        self.assertIn("current_remote_sha256 != remote_runner_sha256", source)
        self.assertGreaterEqual(source.count('device.shell("am", "force-stop"'), 3)
        self.assertNotIn('"profile": "tablet"', source)

    def test_typed_shared_seam_and_both_phone_targets_are_present(self) -> None:
        contract = (CORE / "Chummer.Contracts/Api/CharacterRosterFavoriteContracts.cs").read_text(
            encoding="utf-8"
        )
        rules = (CORE / "Chummer.Application/Tools/CharacterRosterFavoriteRules.cs").read_text(
            encoding="utf-8"
        )
        presenter = (
            PRESENTATION / "Chummer.Presentation/Overview/CharacterRosterFavoritePresenter.cs"
        ).read_text(encoding="utf-8")
        page = (REPO / "src/Chummer.Android/Native/RosterFavoritesPage.cs").read_text(
            encoding="utf-8"
        )
        coordinator = (REPO / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("enum CharacterRosterSortTarget", contract)
        self.assertIn("record CharacterRosterSortMutation", contract)
        self.assertIn("Comparer<string>.Default.Compare", rules)
        self.assertIn("mutation.ExpectedRevision != current.Revision", rules)
        self.assertIn("CharacterRosterFavoriteRules.ApplySort", presenter)
        self.assertIn("_store.Save(mutation.ExpectedRevision, updated)", presenter)
        self.assertIn('AutomationId = "roster-sort-favorites"', page)
        self.assertIn('AutomationId = "roster-sort-recent"', page)
        self.assertIn("_rosterFavoritePresenter.ApplySort", coordinator)

    def test_fixture_is_public_minimal_and_character_xml_is_not_roster_sort_storage(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        self.assertEqual("Sort Proof", root.findtext("alias"))
        self.assertEqual(
            "character XML must remain unrelated",
            root.findtext("./customstate/rostersort"),
        )
        self.assertIsNone(root.find("rosterfavorites"))
        self.assertIsNone(root.find("recentcharacters"))

    def test_inventory_and_receipt_validation_fail_closed(self) -> None:
        spec = importlib.util.spec_from_file_location("inventory_roster_sort", INVENTORY_SCRIPT)
        assert spec is not None and spec.loader is not None
        inventory = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(inventory)
        specs = inventory._capture_only_phone_e2e_specs(PRESENTATION, CORE)
        roster = specs["roster-sort"]
        self.assertIsNone(inventory._validated_capture_only_phone_e2e_receipt(roster))
        with tempfile.TemporaryDirectory() as temporary:
            forged = Path(temporary) / "receipt.json"
            forged.write_text(json.dumps({"status": "pass", "profile": "phone"}), encoding="utf-8")
            roster = {**roster, "receipt": forged}
            self.assertIsNone(inventory._validated_capture_only_phone_e2e_receipt(roster))

        payload = json.loads(
            (REPO / "docs/ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json").read_text(
                encoding="utf-8"
            )
        )
        rows = [
            row
            for row in payload["rows"]
            if row["legacy"]["formOrControl"] == "CharacterRoster"
            and row["legacy"]["controlName"] == "tsSort"
        ]
        self.assertEqual(1, len(rows))
        row = rows[0]
        self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
        self.assertEqual("RosterFavoritesPage", row["phone"]["surface"])
        self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
        self.assertEqual("missing", row["tablet"]["status"])
        self.assertFalse(row["completionProven"])


if __name__ == "__main__":
    unittest.main()
