import ast
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests/run_api36_career_edge_use_e2e.py"
FIXTURE = REPO / "tests/fixtures/career-edge-use-e2e.chum5"


class Api36CareerEdgeUseDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_api36_and_digest_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertIn('CONTROLS = ("cmdEdgeSpent", "cmdEdgeGained")', source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "career-edge-use"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('"build-career-edge-use"', source)
        self.assertIn('"career-edge-use-spend"', source)
        self.assertIn('"career-edge-use-regain"', source)
        self.assertGreaterEqual(source.count('device.shell("am", "force-stop"'), 2)
        self.assertIn('assert_button_state(device, "career-edge-use-regain", False)', source)
        self.assertIn('assert_button_state(device, "career-edge-use-spend", False)', source)
        self.assertIn('for expected_used in range(1, 5):', source)
        self.assertIn('"careerEdgeUseRulesSha256"', source)
        self.assertIn('"presenterPersistenceSha256"', source)
        self.assertIn('"workspaceStoreSha256"', source)
        self.assertIn('"controlCount": len(controls)', source)
        self.assertNotIn('"tablet"', source)

    def test_fixture_has_exact_career_edge_identity_and_unrelated_xml(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        self.assertEqual("True", root.findtext("created"))
        self.assertEqual("1", root.findtext("edgeused"))
        edge = [
            attribute for attribute in root.findall("./attributes/attribute")
            if attribute.findtext("name") == "EDG"
        ]
        self.assertEqual(1, len(edge))
        self.assertEqual("4", edge[0].findtext("totalvalue"))
        self.assertEqual(
            "Unrelated nested Edge use must survive",
            root.findtext("./customstate/edgeused"),
        )


if __name__ == "__main__":
    unittest.main()
