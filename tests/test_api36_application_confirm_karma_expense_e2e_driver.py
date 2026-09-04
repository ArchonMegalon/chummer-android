import ast
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET

from tests.test_api36_roster_sort_e2e_driver import _resolve_repository_sibling


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests/run_api36_application_confirm_karma_expense_e2e.py"
FIXTURE = REPO / "tests/fixtures/application-confirm-karma-expense-e2e.chum5"
INVENTORY_SCRIPT = REPO / "scripts/materialize_chummer5_editability_inventory.py"
PRESENTATION = _resolve_repository_sibling(REPO.parent, "presentation", ("presentation", "chummer-presentation"))
CORE = _resolve_repository_sibling(REPO.parent, "core", ("core", "chummer-core-engine"))


class Api36ApplicationConfirmKarmaExpenseDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_api36_arm64_package_and_digest_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertIn('CONTROL = "chkConfirmKarmaExpense"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('abi != "arm64-v8a"', source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "application-confirm-karma-expense"', source)
        self.assertIn('device.shell("pm", "path", shared.PACKAGE)', source)
        self.assertIn('"applicationSettingsStoreSha256"', source)
        self.assertIn('"runnerFixtureSha256"', source)
        self.assertIn("current_remote_sha256 != remote_runner_sha256", source)
        self.assertGreaterEqual(source.count('device.shell("am", "force-stop"'), 3)
        self.assertNotIn('"profile": "tablet"', source)

    def test_legacy_value_remains_readable_but_is_not_exposed_on_phone(self) -> None:
        contract = (CORE / "Chummer.Contracts/Api/ApplicationDeleteConfirmationContracts.cs").read_text(encoding="utf-8")
        rules = (CORE / "Chummer.Application/Tools/ApplicationDeleteConfirmationRules.cs").read_text(encoding="utf-8")
        store = (CORE / "Chummer.Infrastructure/Files/FileApplicationDeleteConfirmationStore.cs").read_text(encoding="utf-8")
        presenter = (PRESENTATION / "Chummer.Presentation/Overview/ApplicationDeleteConfirmationPresenter.cs").read_text(encoding="utf-8")
        page = (REPO / "src/Chummer.Android/Native/ApplicationSettingsPage.cs").read_text(encoding="utf-8")
        coordinator = (REPO / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        self.assertIn("ConfirmKarmaExpense = true", contract)
        self.assertIn("record ApplicationConfirmationSettingsMutation", contract)
        self.assertIn('LegacyKarmaExpenseIdentity = "confirmkarmaexpense"', rules)
        self.assertIn("ApplySnapshot", rules)
        self.assertIn("mutation.ExpectedRevision != current.Revision", rules)
        self.assertIn("confirmKarmaExpense = true", store)
        self.assertIn('TryGetProperty("ConfirmKarmaExpense"', store)
        self.assertIn("ApplicationDeleteConfirmationRules.ApplySnapshot", presenter)
        self.assertIn("_store.Save(mutation.ExpectedRevision, updated)", presenter)
        self.assertNotIn('AutomationId = "settings-confirm-karma-expense"', page)
        self.assertNotIn("_confirmKarmaExpense", page)
        self.assertNotIn("SaveApplicationConfirmationSettingsAsync", coordinator)
        self.assertIn("SaveDeleteConfirmationSettingAsync", page)
        self.assertIn("Flush(flushToDisk: true)", store)
        self.assertIn("File.Replace", store)
        self.assertIn('path + ".bak"', store)

    def test_fixture_is_public_minimal_and_character_xml_is_not_settings_storage(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        self.assertEqual("Confirm Karma Expense Proof", root.findtext("alias"))
        self.assertEqual(
            "character XML must remain unrelated",
            root.findtext("./customstate/confirmkarmaexpense"),
        )
        self.assertIsNone(root.find("applicationsettings"))

    def test_inventory_and_receipt_validation_fail_closed(self) -> None:
        spec = importlib.util.spec_from_file_location("inventory_application_confirm_karma", INVENTORY_SCRIPT)
        assert spec is not None and spec.loader is not None
        inventory = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(inventory)
        specs = inventory._capture_only_phone_e2e_specs(PRESENTATION, CORE)
        settings = specs["application-confirm-karma-expense"]
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
            and row["legacy"]["controlName"] == "chkConfirmKarmaExpense"
        ]
        self.assertEqual(1, len(rows))
        row = rows[0]
        self.assertEqual("missing", row["phone"]["status"])
        self.assertIsNone(row["phone"]["surface"])
        self.assertIsNone(row["phone"]["automationId"])
        self.assertEqual("missing", row["e2e"]["phone"]["status"])
        self.assertEqual("missing", row["tablet"]["status"])
        self.assertIn("Deliberately not exposed on Android phone", row["phone"]["coverageLimit"])
        self.assertFalse(row["completionProven"])


if __name__ == "__main__":
    unittest.main()
