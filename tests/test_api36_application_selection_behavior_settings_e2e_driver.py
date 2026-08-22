import ast
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

from tests.test_api36_roster_sort_e2e_driver import _resolve_repository_sibling


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests/run_api36_application_selection_behavior_settings_e2e.py"
FIXTURE = REPO / "tests/fixtures/application-selection-behavior-settings-e2e.chum5"
INVENTORY_SCRIPT = REPO / "scripts/materialize_chummer5_editability_inventory.py"
PRESENTATION = _resolve_repository_sibling(
    REPO.parent, "presentation", ("presentation", "chummer-presentation")
)
CORE = _resolve_repository_sibling(REPO.parent, "core", ("core", "chummer-core-engine"))
CHUMMER5 = Path("/docker/chummer5a")
CONTROLS = {
    "chkSearchInCategoryOnly": "settings-search-in-category-only",
    "chkAllowEasterEggs": "settings-allow-easter-eggs",
}


class Api36ApplicationSelectionBehaviorSettingsDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_api36_arm64_digest_bound_and_not_a_receipt(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        for control in CONTROLS:
            self.assertIn(f'"EditGlobalSettings.{control}"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('abi != "arm64-v8a"', source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "application-selection-behavior-settings"', source)
        self.assertIn('device.shell("pm", "path", shared.PACKAGE)', source)
        self.assertIn('"applicationSettingsStoreSha256"', source)
        self.assertIn('"runnerFixtureSha256"', source)
        self.assertIn("LEGACY_REVISION", source)
        self.assertIn("LEGACY_SOURCE_DIGESTS", source)
        self.assertIn("current_remote_sha256 != remote_runner_sha256", source)
        self.assertGreaterEqual(source.count('device.shell("am", "force-stop"'), 3)
        self.assertNotIn('"profile": "tablet"', source)

    def test_typed_whole_page_defaults_and_atomic_store_are_present(self) -> None:
        contract = (
            CORE / "Chummer.Contracts/Api/ApplicationDeleteConfirmationContracts.cs"
        ).read_text(encoding="utf-8")
        rules = (
            CORE / "Chummer.Application/Tools/ApplicationDeleteConfirmationRules.cs"
        ).read_text(encoding="utf-8")
        store = (
            CORE / "Chummer.Infrastructure/Files/FileApplicationDeleteConfirmationStore.cs"
        ).read_text(encoding="utf-8")
        presenter = (
            PRESENTATION / "Chummer.Presentation/Overview/ApplicationDeleteConfirmationPresenter.cs"
        ).read_text(encoding="utf-8")
        page = (REPO / "src/Chummer.Android/Native/ApplicationSettingsPage.cs").read_text(
            encoding="utf-8"
        )
        coordinator = (
            REPO / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs"
        ).read_text(encoding="utf-8")

        for identity in ("SearchInCategoryOnly", "AllowEasterEggs"):
            self.assertIn(identity, contract)
            self.assertIn(f"ApplicationSettingIdentity.{identity}", coordinator)
            self.assertIn(f'TryGetProperty("{identity}"', store)
        self.assertIn("bool SearchInCategoryOnly = true", contract)
        self.assertIn("bool AllowEasterEggs = false", contract)
        self.assertIn('LegacySearchInCategoryOnlyIdentity = "searchincategoryonly"', rules)
        self.assertIn('LegacyAllowEasterEggsIdentity = "alloweastereggs"', rules)
        self.assertIn(
            "RequireIdentity(mutation.SearchInCategoryOnly, ApplicationSettingIdentity.SearchInCategoryOnly)",
            rules,
        )
        self.assertIn(
            "RequireIdentity(mutation.AllowEasterEggs, ApplicationSettingIdentity.AllowEasterEggs)",
            rules,
        )
        self.assertIn("ApplicationDeleteConfirmationRules.ApplySettingsSnapshot", presenter)
        self.assertIn("_store.Save(mutation.ExpectedRevision, updated)", presenter)
        self.assertIn("SaveApplicationSettingsAsync", page)
        for automation_id in CONTROLS.values():
            self.assertIn(f'AutomationId = "{automation_id}"', page)
        self.assertIn("Flush(flushToDisk: true)", store)
        self.assertIn("File.Replace", store)
        self.assertIn('path + ".bak"', store)

    def test_exact_canonical_default_load_dirty_save_authority_is_fail_closed(self) -> None:
        spec = importlib.util.spec_from_file_location("inventory_selection_behavior", INVENTORY_SCRIPT)
        assert spec is not None and spec.loader is not None
        inventory = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(inventory)

        self.assertTrue(inventory._application_selection_behavior_legacy_authority(CHUMMER5))
        authority = inventory.APPLICATION_SELECTION_BEHAVIOR_LEGACY_AUTHORITY
        drifted = {
            **authority,
            "methodDigests": {
                **authority["methodDigests"],
                ("Chummer/Forms/EditGlobalSettings.cs", "OptionsChanged"): "0" * 64,
            },
        }
        with patch.object(inventory, "APPLICATION_SELECTION_BEHAVIOR_LEGACY_AUTHORITY", drifted):
            self.assertFalse(inventory._application_selection_behavior_legacy_authority(CHUMMER5))

    def test_fixture_is_public_minimal_and_character_xml_is_not_settings_storage(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        self.assertEqual("Application Selection Behavior Proof", root.findtext("alias"))
        self.assertEqual(
            "character XML must remain unrelated",
            root.findtext("./customstate/selectionbehavior"),
        )
        self.assertIsNone(root.find("applicationsettings"))

    def test_inventory_and_receipt_validation_fail_closed_for_exact_two_rows(self) -> None:
        spec = importlib.util.spec_from_file_location("inventory_selection_behavior", INVENTORY_SCRIPT)
        assert spec is not None and spec.loader is not None
        inventory = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(inventory)
        specs = inventory._capture_only_phone_e2e_specs(PRESENTATION, CORE)
        settings = specs["application-selection-behavior-settings"]
        self.assertIsNone(inventory._validated_capture_only_phone_e2e_receipt(settings))
        with tempfile.TemporaryDirectory() as temporary:
            forged = Path(temporary) / "receipt.json"
            forged.write_text(json.dumps({"status": "pass", "profile": "phone"}), encoding="utf-8")
            settings = {**settings, "receipt": forged}
            self.assertIsNone(inventory._validated_capture_only_phone_e2e_receipt(settings))

        payload = json.loads(
            (REPO / "docs/ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json").read_text(
                encoding="utf-8"
            )
        )
        rows = [
            row
            for row in payload["rows"]
            if row["legacy"]["formOrControl"] == "EditGlobalSettings"
            and row["legacy"]["controlName"] in CONTROLS
        ]
        self.assertEqual(2, len(rows))
        self.assertEqual(set(CONTROLS), {row["legacy"]["controlName"] for row in rows})
        for row in rows:
            control = row["legacy"]["controlName"]
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual("ApplicationSettingsPage", row["phone"]["surface"])
            self.assertEqual(CONTROLS[control], row["phone"]["automationId"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertIn("exact canonical Chummer5", row["phone"]["coverageLimit"])
            self.assertFalse(row["completionProven"])

        source_digests = {
            item["path"]: item["sha256"]
            for item in payload["generationInputs"]["androidAndPresenterSources"]
        }
        for path in (
            "src/Chummer.Android/Native/ApplicationSettingsPage.cs",
            "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
            "chummer-presentation/Chummer.Presentation/Overview/ApplicationDeleteConfirmationPresenter.cs",
            "chummer-core-engine/Chummer.Contracts/Api/ApplicationDeleteConfirmationContracts.cs",
            "chummer-core-engine/Chummer.Application/Tools/ApplicationDeleteConfirmationRules.cs",
            "chummer-core-engine/Chummer.Application/Tools/IApplicationDeleteConfirmationStore.cs",
            "chummer-core-engine/Chummer.Infrastructure/Files/FileApplicationDeleteConfirmationStore.cs",
            "tests/run_api36_application_selection_behavior_settings_e2e.py",
            "tests/fixtures/application-selection-behavior-settings-e2e.chum5",
        ):
            self.assertRegex(source_digests[path], r"^[0-9a-f]{64}$")


if __name__ == "__main__":
    unittest.main()
