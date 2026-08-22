import ast
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_improvement_group_add_e2e.py"
CAREER = REPO / "tests" / "fixtures" / "career-improvement-group-add-e2e.chum5"
CREATION = REPO / "tests" / "fixtures" / "creation-improvement-group-add-negative-e2e.chum5"
CONTROLS = ("CharacterCareer.cmdAddImprovementGroup",)


class Api36ImprovementGroupAddE2EDriverTests(unittest.TestCase):
    def test_driver_is_digest_phone_api36_arm64_package_and_restart_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        module = ast.parse(source)
        controls_assignment = next(
            node for node in module.body
            if isinstance(node, ast.Assign)
            and any(isinstance(target, ast.Name) and target.id == "CONTROLS" for target in node.targets)
        )
        self.assertEqual(CONTROLS, tuple(ast.literal_eval(controls_assignment.value)))
        self.assertIn('if api != "36"', source)
        self.assertIn('if abi != "arm64-v8a"', source)
        self.assertIn('PACKAGE = "com.myexternalbrain.chummer"', source)
        self.assertIn('"package": shared.PACKAGE', source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "improvement-group-add"', source)
        self.assertIn('device.shell("am", "force-stop"', source)
        self.assertIn('"careerExactGroupAppended": "pass"', source)
        self.assertIn('"careerDuplicateGroupAppended": "pass"', source)
        self.assertIn('"creationActionNotExposed": "pass"', source)
        for digest in (
            "improvementGroupAddPageSha256",
            "improvementGroupAddContractSha256",
            "improvementGroupAddRulesSha256",
            "presenterPersistenceSha256",
            "workspaceStoreSha256",
            "careerFixtureSha256",
            "creationNegativeFixtureSha256",
        ):
            self.assertIn(f'"{digest}"', source)
        self.assertNotIn('"tablet"', source)
        self.assertEqual(1, source.count("args.receipt.write_text"))
        self.assertLess(
            source.rindex("assert_creation_negative(device"),
            source.rindex("receipt = {"),
        )
        self.assertIn("raise SystemExit(1)", source)

    def test_fixtures_cover_exact_career_collection_economics_and_creation_negative(self) -> None:
        career = ET.parse(CAREER).getroot()
        creation = ET.parse(CREATION).getroot()
        self.assertEqual("True", career.findtext("created"))
        self.assertEqual("False", creation.findtext("created"))
        self.assertEqual(
            ["Alpha", "Beta"],
            [node.text for node in career.findall("./improvementgroups/improvementgroup")],
        )
        self.assertEqual(["Alpha"], [node.text for node in creation.findall("./improvementgroups/improvementgroup")])
        self.assertEqual(("23", "4567.89"), (career.findtext("karma"), career.findtext("nuyen")))
        self.assertEqual(("17", "1234.56"), (creation.findtext("karma"), creation.findtext("nuyen")))
        self.assertEqual("Career group add sentinel", career.findtext("./improvements/improvement/notes"))
        self.assertEqual("Creation group add negative sentinel", creation.findtext("./improvements/improvement/notes"))


if __name__ == "__main__":
    unittest.main()
