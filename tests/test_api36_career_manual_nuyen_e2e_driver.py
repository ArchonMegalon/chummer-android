import ast
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests/run_api36_career_manual_nuyen_e2e.py"
FIXTURE = REPO / "tests/fixtures/career-manual-nuyen-e2e.chum5"


class Api36CareerManualNuyenDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_api36_full_graph_and_restart_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertIn('CONTROLS = ("cmdNuyenGained", "cmdNuyenSpent")', source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "career-manual-nuyen"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('"careerManualNuyenRulesSha256"', source)
        self.assertIn('"sourceResolverSha256"', source)
        self.assertIn('"presenterPersistenceSha256"', source)
        self.assertIn('"workspaceStoreSha256"', source)
        self.assertGreaterEqual(source.count('device.shell("am", "force-stop"'), 2)
        self.assertIn('nuyen_row.findtext("amount") != "-3000"', source)
        self.assertIn('karma_row.findtext("amount") != "2"', source)
        self.assertNotIn('"tablet"', source)

    def test_fixture_has_exact_career_balances_expense_and_unrelated_xml(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        self.assertEqual("True", root.findtext("created"))
        self.assertEqual("5", root.findtext("karma"))
        self.assertEqual("10000", root.findtext("nuyen"))
        self.assertEqual("2020-01-01T00:00:00", root.findtext("./expenses/expense/date"))
        self.assertEqual(
            "Unrelated nested Nuyen must survive",
            root.findtext("./customstate/nuyen"),
        )


if __name__ == "__main__":
    unittest.main()
