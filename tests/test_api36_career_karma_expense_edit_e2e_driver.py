import ast
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests/run_api36_career_karma_expense_edit_e2e.py"
FIXTURE = REPO / "tests/fixtures/career-karma-expense-edit-e2e.chum5"
MANUAL_ID = "65da27db-24a8-4b6e-b42c-30f4bb13a4f8"
LOCKED_ID = "a47497a9-0893-43e1-89cb-fb2dfa803b5d"
NUYEN_SIBLING_ID = "d1616d91-6848-49bd-a513-9b52d3399787"


class Api36CareerKarmaExpenseEditDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_api36_full_graph_revision_digest_and_restart_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertIn(
            'CONTROLS = ("cmdKarmaEdit", "lstKarma", "tsEditKarmaExpense")',
            source,
        )
        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "career-karma-expense-edit"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('abi != "x86_64"', source)
        self.assertIn('"package": shared.PACKAGE', source)
        self.assertIn('"careerKarmaExpenseRulesSha256"', source)
        self.assertIn('"careerKarmaExpenseMutationSha256"', source)
        self.assertIn('"localizationCatalogSha256"', source)
        self.assertIn('"presenterPersistenceSha256"', source)
        self.assertIn('"workspaceStoreSha256"', source)
        self.assertEqual(2, source.count('device.shell("pm", "clear"'))
        self.assertEqual(2, source.count("shared.force_stop_and_launch_new_process"))
        self.assertIn("saved.content_revision != previous.content_revision + 1", source)
        self.assertIn("shared.require_saved_authority(saved)", source)
        self.assertIn("payload_sha256(payload) == authority.payload_sha256", source)
        self.assertIn("shared.require_restored_authority(saved, restored)", source)
        self.assertIn("shared.require_restored_authority(locked_saved, restored)", source)
        self.assertIn('manual.findtext("amount") != "1.9"', source)
        self.assertIn('root.findtext("karma") != "10"', source)
        self.assertIn('manual.findtext("amount") != "2"', source)
        self.assertIn('rounded_root.findtext("karma") != "11"', source)
        self.assertIn('amount_node.attributes.get("enabled") != "false"', source)
        self.assertIn("device.tap(LOCKED_ID", source)
        self.assertNotIn(
            'device.tap("2081-05-10 10:00:00 · 3.5 Karma · Locked quality"',
            source,
        )
        self.assertNotIn('"tablet"', source)

    def test_fixture_has_exact_manual_locked_nuyen_and_nested_authorities(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        self.assertEqual("True", root.findtext("created"))
        self.assertEqual("10", root.findtext("karma"))
        expenses = root.findall("./expenses/expense")
        self.assertEqual(3, len(expenses))
        by_id = {expense.findtext("guid"): expense for expense in expenses}
        self.assertEqual({MANUAL_ID, LOCKED_ID, NUYEN_SIBLING_ID}, set(by_id))
        self.assertEqual("1.9", by_id[MANUAL_ID].findtext("amount"))
        self.assertEqual("ManualAdd", by_id[MANUAL_ID].findtext("./undo/karmatype"))
        self.assertEqual("3.5", by_id[LOCKED_ID].findtext("amount"))
        self.assertEqual("ImproveAttribute", by_id[LOCKED_ID].findtext("./undo/karmatype"))
        self.assertEqual("Nuyen", by_id[NUYEN_SIBLING_ID].findtext("type"))
        self.assertEqual("keep-nuyen", by_id[NUYEN_SIBLING_ID].findtext("custom"))
        self.assertEqual(
            "Unrelated nested Karma must survive",
            root.findtext("./customstate/karma"),
        )
        sentinel = root.find("./customstate/sentinel")
        self.assertIsNotNone(sentinel)
        self.assertEqual("nested-sentinel", sentinel.get("guid"))
        self.assertEqual("keep-nested-structure", sentinel.text)


if __name__ == "__main__":
    unittest.main()
