import ast
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET

from tests.test_api36_roster_sort_e2e_driver import _resolve_repository_sibling


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests/run_api36_application_confirm_delete_e2e.py"
FIXTURE = REPO / "tests/fixtures/application-confirm-delete-e2e.chum5"
INVENTORY_SCRIPT = REPO / "scripts/materialize_chummer5_editability_inventory.py"
PRESENTATION = _resolve_repository_sibling(
    REPO.parent,
    "presentation",
    ("presentation", "chummer-presentation"),
)
CORE = _resolve_repository_sibling(
    REPO.parent,
    "core",
    ("core", "chummer-core-engine"),
)


class Api36ApplicationConfirmDeleteDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_api36_arm64_package_and_digest_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertIn('CONTROLS = ("chkConfirmDelete", "cmdOK")', source)
        self.assertIn('api != "36"', source)
        self.assertIn('abi != "arm64-v8a"', source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "application-confirm-delete"', source)
        self.assertIn('device.shell("pm", "path", shared.PACKAGE)', source)
        self.assertIn('"applicationSettingsStoreSha256"', source)
        self.assertIn('"runnerFixtureSha256"', source)
        self.assertIn("current_remote_sha256 != remote_runner_sha256", source)
        self.assertGreaterEqual(source.count('device.shell("am", "force-stop"'), 3)
        self.assertNotIn('"profile": "tablet"', source)

    def test_typed_shared_seam_staging_and_newest_valid_recovery_are_present(self) -> None:
        contract = (CORE / "Chummer.Contracts/Api/ApplicationDeleteConfirmationContracts.cs").read_text(encoding="utf-8")
        rules = (CORE / "Chummer.Application/Tools/ApplicationDeleteConfirmationRules.cs").read_text(encoding="utf-8")
        store = (CORE / "Chummer.Infrastructure/Files/FileApplicationDeleteConfirmationStore.cs").read_text(encoding="utf-8")
        presenter = (PRESENTATION / "Chummer.Presentation/Overview/ApplicationDeleteConfirmationPresenter.cs").read_text(encoding="utf-8")
        page = (REPO / "src/Chummer.Android/Native/ApplicationSettingsPage.cs").read_text(encoding="utf-8")
        coordinator = (REPO / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        self.assertIn("enum ApplicationSettingIdentity", contract)
        self.assertIn("ConfirmDelete: true", contract)
        self.assertIn('LegacyIdentity = "confirmdelete"', rules)
        self.assertIn("mutation.ExpectedRevision != current.Revision", rules)
        self.assertIn("_store.Save(mutation.ExpectedRevision, updated)", presenter)
        self.assertIn('AutomationId = "settings-confirm-delete"', page)
        self.assertIn('AutomationId = "settings-save"', page)
        self.assertNotIn("_confirmDelete.Toggled", page)
        self.assertIn("SaveDeleteConfirmationSettingAsync", coordinator)
        self.assertIn("primary.Revision >= backup.Revision", store)
        self.assertIn("Flush(flushToDisk: true)", store)
        self.assertIn("File.Replace", store)
        self.assertIn('path + ".bak"', store)

    def test_fixture_is_public_minimal_and_character_xml_is_not_settings_storage(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        self.assertEqual("Confirm Delete Proof", root.findtext("alias"))
        self.assertEqual(
            "character XML must remain unrelated",
            root.findtext("./customstate/applicationconfirmdelete"),
        )
        self.assertIsNone(root.find("confirmdelete"))
        self.assertIsNone(root.find("applicationsettings"))

    def test_inventory_and_receipt_validation_fail_closed(self) -> None:
        spec = importlib.util.spec_from_file_location("inventory_application_confirm_delete", INVENTORY_SCRIPT)
        assert spec is not None and spec.loader is not None
        inventory = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(inventory)
        specs = inventory._capture_only_phone_e2e_specs(PRESENTATION, CORE)
        settings = specs["application-confirm-delete"]
        self.assertIsNone(inventory._validated_capture_only_phone_e2e_receipt(settings))
        with tempfile.TemporaryDirectory() as temporary:
            forged = Path(temporary) / "receipt.json"
            forged.write_text(json.dumps({"status": "pass", "profile": "phone"}), encoding="utf-8")
            settings = {**settings, "receipt": forged}
            self.assertIsNone(inventory._validated_capture_only_phone_e2e_receipt(settings))

        payload = json.loads((REPO / "docs/ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json").read_text(encoding="utf-8"))
        rows = [
            row for row in payload["rows"]
            if row["legacy"]["formOrControl"] == "EditGlobalSettings"
            and row["legacy"]["controlName"] in {"chkConfirmDelete", "cmdOK"}
        ]
        self.assertEqual(2, len(rows))
        for row in rows:
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual("ApplicationSettingsPage", row["phone"]["surface"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertIn("confirmdelete only", row["phone"]["coverageLimit"])
            self.assertFalse(row["completionProven"])


if __name__ == "__main__":
    unittest.main()
