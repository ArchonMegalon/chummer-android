import ast
import base64
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_career_mugshot_state_e2e.py"
FIXTURE = REPO / "tests" / "fixtures" / "career-mugshot-state-e2e.chum5"
PAGE = REPO / "src" / "Chummer.Android" / "Native" / "CareerMugshotPage.cs"
BUILD_PAGE = REPO / "src" / "Chummer.Android" / "Native" / "BuildPage.cs"
CONTROLS = (
    "CharacterCareer.nudMugshotIndex",
    "CharacterCareer.chkIsMainMugshot",
    "CharacterCareer.cmdDeleteMugshot",
)


class Api36CareerMugshotStateE2EDriverTests(unittest.TestCase):
    def test_driver_is_career_phone_api36_arm64_restart_and_digest_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        module = ast.parse(source)
        assignment = next(
            node for node in module.body
            if isinstance(node, ast.Assign)
            and any(isinstance(target, ast.Name) and target.id == "CONTROLS" for target in node.targets)
        )
        self.assertEqual(CONTROLS, tuple(ast.literal_eval(assignment.value)))
        for marker in (
            'if api != "36"',
            '"arm64-v8a" not in abi_list.split(",")',
            '"abi": "arm64-v8a"',
            '"package": shared.PACKAGE',
            '"profile": "phone"',
            '"journey": "career-mugshot-state"',
            'device.shell("am", "force-stop"',
            '"oneBasedWrapSelection": "pass"',
            '"mainIndexSetFromSelected": "pass"',
            '"mainIndexClearedFromSelected": "pass"',
            '"selectedMugshotDeleted": "pass"',
            '"mainIndexAdjustedAfterEarlierDelete": "pass"',
            '"sameSessionReopenAfterDelete": "pass"',
            '"processRestartDeletePersistence": "pass"',
            '"sameSessionReopenCareer": "pass"',
            '"processRestartCareer": "pass"',
        ):
            self.assertIn(marker, source)
        for digest in (
            "apkSha256",
            "driverSha256",
            "careerMugshotPageSha256",
            "careerMugshotContractSha256",
            "careerMugshotRulesSha256",
            "presenterPersistenceSha256",
            "workspaceStoreSha256",
            "careerFixtureSha256",
        ):
            self.assertIn(f'"{digest}"', source)
        self.assertNotIn("CharacterCreate.nudMugshotIndex", source)
        self.assertNotIn("CharacterCreate.chkIsMainMugshot", source)
        self.assertNotIn('"tablet"', source)

    def test_phone_page_preserves_exact_wrap_and_career_only_route(self) -> None:
        page = PAGE.read_text(encoding="utf-8")
        build = BUILD_PAGE.read_text(encoding="utf-8")
        for marker in (
            "CharacterCareerMugshotRules.WrapSelection",
            "CharacterCareerMugshotRules.ResolveSelection",
            "CharacterCareerMugshotRules.IsSelectedMain",
            'AutomationId = "career-mugshot-index"',
            'AutomationId = "career-mugshot-main"',
            'AutomationId = "career-mugshot-delete"',
            'AutomationId = "career-mugshot-save"',
            "CareerMugshotDeleteRequest",
            "CharacterCareerMugshotRules.TryValidateDelete",
        ):
            self.assertIn(marker, page)
        self.assertIn("Coordinator.State.Profile?.Created == true", build)
        self.assertIn('automationId: "build-career-mugshots"', build)
        self.assertIn("new CareerMugshotPage(Coordinator, editor)", build)

    def test_fixture_has_ordered_nonempty_images_and_unrelated_sentinels(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        self.assertEqual("True", root.findtext("created"))
        self.assertEqual("-1", root.findtext("mainmugshotindex"))
        images = [element.text or "" for element in root.findall("./mugshots/mugshot")]
        self.assertEqual(2, len(images))
        decoded = [base64.b64decode(value, validate=True) for value in images]
        self.assertTrue(all(value.startswith(b"\x89PNG\r\n\x1a\n") for value in decoded))
        self.assertNotEqual(decoded[0], decoded[1])
        self.assertEqual(("3141", "27"), (root.findtext("nuyen"), root.findtext("karma")))
        self.assertEqual("Career Mugshot runner sentinel", root.findtext("customstate"))


if __name__ == "__main__":
    unittest.main()
