import ast
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests/run_api36_group_membership_e2e.py"
FIXTURES = (
    REPO / "tests/fixtures/creation-group-membership-e2e.chum5",
    REPO / "tests/fixtures/career-group-membership-e2e.chum5",
)


class Api36GroupMembershipDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_api36_and_digest_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertIn('CONTROL = "chkJoinGroup"', source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "group-membership"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('"build-group-membership"', source)
        self.assertIn('"group-membership-toggle"', source)
        self.assertIn('"group-membership-save"', source)
        self.assertIn('device.tap("Spend & Save"', source)
        self.assertGreaterEqual(source.count('device.shell("am", "force-stop"'), 3)
        self.assertIn('"groupMembershipRulesSha256"', source)
        self.assertIn('"sourceResolverSha256"', source)
        self.assertIn('"presenterPersistenceSha256"', source)
        self.assertIn('"workspaceStoreSha256"', source)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_cover_creation_and_career_without_private_data(self) -> None:
        created = []
        for fixture in FIXTURES:
            root = ET.parse(fixture).getroot()
            created.append(root.findtext("created"))
            self.assertEqual("False", root.findtext("groupmember"))
            self.assertEqual("True", root.findtext("magenabled"))
            self.assertEqual("223a11ff-80e0-428b-89a9-6ef1c243b8b6", root.findtext("settings"))
            self.assertTrue(root.findtext("./customstate/groupmember", default="").endswith("membership text"))
        self.assertEqual(["False", "True"], created)
        self.assertEqual("0", ET.parse(FIXTURES[0]).getroot().findtext("karma"))
        self.assertEqual("8", ET.parse(FIXTURES[1]).getroot().findtext("karma"))


if __name__ == "__main__":
    unittest.main()
