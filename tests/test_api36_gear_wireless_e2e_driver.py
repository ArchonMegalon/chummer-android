import ast
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_gear_wireless_e2e.py"
CAREER = REPO / "tests" / "fixtures" / "career-gear-equipment-e2e.chum5"
COLLECTION_EDITOR = REPO / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs"
CONTROLS = ("CharacterCareer.chkGearWireless",)


class Api36GearWirelessE2EDriverTests(unittest.TestCase):
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
            '"journey": "gear-wireless"',
            'device.shell("am", "force-stop"',
            '"careerRecursiveGearEdited": "pass"',
            '"zeroEconomicDeltaCareer": "pass"',
            '"sameSessionReopenCareer": "pass"',
            '"processRestartCareer": "pass"',
        ):
            self.assertIn(marker, source)
        for digest in (
            "apkSha256",
            "driverSha256",
            "gearWirelessPageSha256",
            "gearWirelessContractSha256",
            "gearWirelessRulesSha256",
            "presenterPersistenceSha256",
            "workspaceStoreSha256",
            "careerFixtureSha256",
        ):
            self.assertIn(f'"{digest}"', source)
        self.assertNotIn("CharacterCreate.chkGearWireless", source)
        self.assertNotIn('"tablet"', source)

    def test_phone_route_is_explicitly_career_only_and_generic_toggle_is_suppressed(self) -> None:
        source = COLLECTION_EDITOR.read_text(encoding="utf-8")
        self.assertIn("AddGearWirelessAction(item);", source)
        self.assertIn("Coordinator.State.Profile?.Created != true", source)
        self.assertIn('automationId: $"gear-wireless-open-{gearId:N}"', source)
        self.assertIn("new GearWirelessPage(Coordinator, editor)", source)
        self.assertIn("value.Field is WorkspaceCollectionToggleField.Equipped", source)
        self.assertIn("or WorkspaceCollectionToggleField.WirelessEnabled", source)
        self.assertIn("_toggleInputs.TryGetValue", source)

    def test_career_fixture_binds_recursive_identity_wireless_and_zero_economics(self) -> None:
        root = ET.parse(CAREER).getroot()
        self.assertEqual("True", root.findtext("created"))
        self.assertEqual(("8765", "19"), (root.findtext("nuyen"), root.findtext("karma")))
        nodes = root.findall(".//gear")
        identities = [node.findtext("guid", default="") for node in nodes]
        self.assertEqual(len(identities), len(set(identities)))
        self.assertTrue(all(node.findtext("wirelesson") in {"True", "False"} for node in nodes))
        self.assertIsNotNone(root.find("./gears/gear/children/gear"))


if __name__ == "__main__":
    unittest.main()
