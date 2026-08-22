import ast
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests/run_api36_weapon_matrix_swap_e2e.py"
CAREER = REPO / "tests/fixtures/career-weapon-matrix-swap-e2e.chum5"
CREATION = REPO / "tests/fixtures/creation-weapon-matrix-swap-negative-e2e.chum5"


class Api36WeaponMatrixSwapDriverTests(unittest.TestCase):
    def test_driver_is_four_row_career_only_phone_api36_arm64_digest_restart_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        module = ast.parse(source)
        controls = next(node for node in module.body if isinstance(node, ast.Assign)
                        and any(isinstance(target, ast.Name) and target.id == "CONTROLS"
                                for target in node.targets))
        self.assertEqual(
            (
                "CharacterCareer.cboWeaponGearAttack",
                "CharacterCareer.cboWeaponGearSleaze",
                "CharacterCareer.cboWeaponGearDataProcessing",
                "CharacterCareer.cboWeaponGearFirewall",
            ),
            tuple(ast.literal_eval(controls.value)),
        )
        journeys = next(node for node in module.body if isinstance(node, ast.Assign)
                        and any(isinstance(target, ast.Name) and target.id == "JOURNEYS"
                                for target in node.targets))
        values = [{keyword.arg: ast.literal_eval(keyword.value) for keyword in journey.keywords}
                  for journey in journeys.value.elts]
        self.assertEqual(
            ("career-attack", "career-sleaze", "career-dp", "career-firewall"),
            tuple(value["name"] for value in values),
        )
        self.assertEqual(
            {"Attack", "Sleaze", "Data Processing", "Firewall"},
            {value["changed"] for value in values},
        )
        for marker in (
            'if api != "36"',
            '"arm64-v8a" not in abi.split(",")',
            '"profile": "phone"',
            '"journey": "weapon-matrix-swap"',
            'device.shell("am", "force-stop"',
            '"creationActionNotExposed": "pass"',
            '"descendantAndOtherOwnerTargetsFailClosedCoverage": "pass"',
            '"weaponMatrixRulesSha256"',
            '"sharedMatrixAuthoritySha256"',
            '"presenterPersistenceSha256"',
            '"workspaceStoreSha256"',
            '"careerFixtureSha256"',
            '"creationNegativeFixtureSha256"',
        ):
            self.assertIn(marker, source)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_pin_career_direct_identity_descendants_and_creation_negative(self) -> None:
        career = ET.parse(CAREER).getroot()
        creation = ET.parse(CREATION).getroot()
        self.assertEqual("True", career.findtext("created"))
        self.assertEqual("False", creation.findtext("created"))
        target = career.find("./weapons/weapon")
        self.assertIsNotNone(target)
        for field in (
            "attack", "sleaze", "dataprocessing", "firewall", "attributearray",
            "canswapattributes", "modattack", "modsleaze", "moddataprocessing",
            "modfirewall", "rating", "cost", "active", "homenode", "notes",
        ):
            self.assertTrue(target.findtext(field), field)
        self.assertEqual("True", target.findtext("canswapattributes"))
        self.assertIsNotNone(target.find("./underbarrel/weapon"))
        self.assertIsNotNone(target.find("./accessories/accessory/gears/gear"))
        ids = [node.findtext("guid") for node in career.iter() if node.find("guid") is not None]
        self.assertEqual(len(ids), len(set(ids)))
        creation_target = creation.find("./weapons/weapon")
        self.assertEqual("Creation target must remain unchanged", creation_target.findtext("notes"))


if __name__ == "__main__":
    unittest.main()
