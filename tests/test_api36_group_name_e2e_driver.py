import ast
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_group_name_e2e.py"
FIXTURES = (
    REPO / "tests" / "fixtures" / "creation-group-name-e2e.chum5",
    REPO / "tests" / "fixtures" / "career-group-name-e2e.chum5",
)


class Api36GroupNameDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_api36_digest_bound_and_restart_safe(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        for marker in (
            '"CharacterCreate.txtGroupName"',
            '"CharacterCareer.txtGroupName"',
            '"profile": "phone"',
            '"journey": "group-name"',
            'api != "36"',
            '"build-group-name"',
            '"group-name-value"',
            '"group-name-save"',
            '"groupNameRulesSha256"',
            '"presenterPersistenceSha256"',
            '"workspaceStoreSha256"',
            '"contactGroupNameNotCrossWired": "pass"',
        ):
            self.assertIn(marker, source)
        self.assertGreaterEqual(source.count('device.shell("am", "force-stop"'), 1)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_cover_both_modes_and_preserve_nested_group_name(self) -> None:
        created = []
        for fixture in FIXTURES:
            root = ET.parse(fixture).getroot()
            created.append(root.findtext("created"))
            self.assertTrue(root.findtext("groupname", default="").endswith("Old Circle"))
            self.assertTrue(root.findtext("./customstate/groupname", default="").endswith("unrelated group text"))
        self.assertEqual(["False", "True"], created)


if __name__ == "__main__":
    unittest.main()
