import ast
import base64
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_creation_mugshot_state_e2e.py"
FIXTURE = REPO / "tests" / "fixtures" / "creation-mugshot-state-e2e.chum5"
PAGE = REPO / "src" / "Chummer.Android" / "Native" / "CreationMugshotPage.cs"
BUILD_PAGE = REPO / "src" / "Chummer.Android" / "Native" / "BuildPage.cs"
CONTROLS = (
    "CharacterCreate.nudMugshotIndex",
    "CharacterCreate.chkIsMainMugshot",
    "CharacterCreate.cmdDeleteMugshot",
)


class Api36CreationMugshotStateE2EDriverTests(unittest.TestCase):
    def test_driver_is_creation_phone_api36_arm64_restart_and_digest_bound(self) -> None:
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
            '"journey": "creation-mugshot-state"',
            'device.shell("am", "force-stop"',
            '"oneBasedWrapSelection": "pass"',
            '"mainIndexSetFromSelected": "pass"',
            '"mainIndexClearedFromSelected": "pass"',
            '"selectedMugshotDeleted": "pass"',
            '"mainIndexAdjustedAfterEarlierDelete": "pass"',
            '"sameSessionReopenAfterDelete": "pass"',
            '"processRestartDeletePersistence": "pass"',
            '"sameSessionReopenCreation": "pass"',
            '"processRestartCreation": "pass"',
        ):
            self.assertIn(marker, source)
        for digest in (
            "apkSha256",
            "driverSha256",
            "creationMugshotPageSha256",
            "creationMugshotContractSha256",
            "creationMugshotRulesSha256",
            "presenterPersistenceSha256",
            "workspaceStoreSha256",
            "creationFixtureSha256",
        ):
            self.assertIn(f'"{digest}"', source)
        self.assertNotIn("CharacterCareer.nudMugshotIndex", source)
        self.assertNotIn("CharacterCareer.chkIsMainMugshot", source)
        self.assertNotIn('"tablet"', source)

    def test_phone_page_preserves_exact_wrap_and_creation_only_route(self) -> None:
        page = PAGE.read_text(encoding="utf-8")
        build = BUILD_PAGE.read_text(encoding="utf-8")
        for marker in (
            "CharacterCreationMugshotRules.WrapSelection",
            "CharacterCreationMugshotRules.ResolveSelection",
            "CharacterCreationMugshotRules.IsSelectedMain",
            'AutomationId = "creation-mugshot-index"',
            'AutomationId = "creation-mugshot-main"',
            'AutomationId = "creation-mugshot-delete"',
            'AutomationId = "creation-mugshot-save"',
            "CreationMugshotDeleteRequest",
            "CharacterCreationMugshotRules.TryValidateDelete",
        ):
            self.assertIn(marker, page)
        self.assertIn("Coordinator.State.Profile?.Created == false", build)
        self.assertIn('automationId: "build-creation-mugshots"', build)
        self.assertIn("new CreationMugshotPage(Coordinator, editor)", build)

    def test_fixture_has_ordered_nonempty_images_and_unrelated_sentinels(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        self.assertEqual("False", root.findtext("created"))
        self.assertEqual("-1", root.findtext("mainmugshotindex"))
        images = [element.text or "" for element in root.findall("./mugshots/mugshot")]
        self.assertEqual(2, len(images))
        decoded = [base64.b64decode(value, validate=True) for value in images]
        self.assertTrue(all(value.startswith(b"\x89PNG\r\n\x1a\n") for value in decoded))
        self.assertNotEqual(decoded[0], decoded[1])
        self.assertEqual(("3141", "27"), (root.findtext("nuyen"), root.findtext("karma")))
        self.assertEqual("Creation Mugshot runner sentinel", root.findtext("customstate"))


if __name__ == "__main__":
    unittest.main()
