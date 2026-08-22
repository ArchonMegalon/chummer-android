import ast
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests/run_api36_gear_attack_swap_e2e.py"
CREATION = REPO / "tests/fixtures/creation-gear-attack-swap-e2e.chum5"
CAREER = REPO / "tests/fixtures/career-gear-attack-swap-e2e.chum5"
CONTROLS = ("CharacterCreate.cboGearAttack", "CharacterCareer.cboGearAttack")


class Api36GearAttackSwapE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_api36_arm64_package_restart_and_digest_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        module = ast.parse(source)
        assignment = next(node for node in module.body if isinstance(node, ast.Assign)
                          and any(isinstance(target, ast.Name) and target.id == "CONTROLS" for target in node.targets))
        self.assertEqual(CONTROLS, tuple(ast.literal_eval(assignment.value)))
        for marker in (
            'if api != "36"', '"arm64-v8a" not in abi_list.split(",")',
            '"package": shared.PACKAGE', '"profile": "phone"', '"journey": "gear-attack-swap"',
            'device.shell("am", "force-stop"', '"creationEligibleNestedGearEdited": "pass"',
            '"careerEligibleNestedGearEdited": "pass"', '"matrixBonusesDisplayOnly"',
            '"attributeArrayAndCanSwapProvenancePreserved"', '"matrixCostAndStatePreserved"',
            '"gearAttackSwapPageSha256"', '"gearAttackSwapContractSha256"',
            '"gearAttackSwapRulesSha256"', '"presenterPersistenceSha256"', '"workspaceStoreSha256"',
        ):
            self.assertIn(marker, source)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_pin_phase_raw_values_bonuses_provenance_identity_and_economics(self) -> None:
        roots = [ET.parse(path).getroot() for path in (CREATION, CAREER)]
        self.assertEqual(["False", "True"], [root.findtext("created") for root in roots])
        for root in roots:
            target = root.find("./gears/gear/children/gear")
            self.assertIsNotNone(target)
            self.assertEqual("True", target.findtext("canswapattributes"))
            self.assertTrue(target.findtext("attributearray"))
            self.assertTrue(target.findtext("modattack"))
            self.assertTrue(target.findtext("moddataprocessing"))
            self.assertTrue(target.findtext("cost"))
            self.assertTrue(root.findtext("nuyen"))
            self.assertTrue(root.findtext("karma"))
            identities = [node.findtext("guid", "") for node in root.findall(".//gear")]
            self.assertEqual(len(identities), len(set(identities)))
        self.assertEqual("7", roots[0].findtext("./gears/gear/children/gear/attack"))
        self.assertEqual("{Rating}", roots[1].findtext("./gears/gear/children/gear/attack"))


if __name__ == "__main__":
    unittest.main()
