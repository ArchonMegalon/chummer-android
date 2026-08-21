import ast
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests/run_api36_career_nuyen_expense_edit_e2e.py"
FIXTURE = REPO / "tests/fixtures/career-nuyen-expense-edit-e2e.chum5"


class Api36CareerNuyenExpenseEditDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_api36_full_graph_and_restart_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertIn('CONTROLS = ("cmdNuyenEdit", "lstNuyen", "tsEditNuyenExpense")', source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "career-nuyen-expense-edit"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('"careerNuyenExpenseRulesSha256"', source)
        self.assertIn('"presenterPersistenceSha256"', source)
        self.assertIn('"workspaceStoreSha256"', source)
        self.assertGreaterEqual(source.count('device.shell("am", "force-stop"'), 2)
        self.assertIn('manual.findtext("amount") != "-175"', source)
        self.assertIn('locked.findtext("amount") != "-500"', source)
        self.assertNotIn('"tablet"', source)

    def test_fixture_has_manual_locked_and_unrelated_xml(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        self.assertEqual("True", root.findtext("created"))
        self.assertEqual("1000", root.findtext("nuyen"))
        expenses = root.findall("./expenses/expense")
        self.assertEqual(2, len(expenses))
        self.assertEqual("ManualSubtract", expenses[0].findtext("./undo/nuyentype"))
        self.assertEqual("AddArmor", expenses[1].findtext("./undo/nuyentype"))
        self.assertEqual("Unrelated nested Nuyen must survive", root.findtext("./customstate/nuyen"))


if __name__ == "__main__":
    unittest.main()
