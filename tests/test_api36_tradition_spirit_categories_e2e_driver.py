import ast
from pathlib import Path
import unittest
import uuid
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_tradition_spirit_categories_e2e.py"
CREATION = REPO / "tests" / "fixtures" / "creation-tradition-spirit-categories-e2e.chum5"
CAREER = REPO / "tests" / "fixtures" / "career-tradition-spirit-categories-e2e.chum5"
CATEGORIES = ("combat", "detection", "health", "illusion", "manipulation")
CONTROLS = tuple(
    f"{form}.cboSpirit{category.title()}"
    for form in ("CharacterCreate", "CharacterCareer")
    for category in CATEGORIES
)
CUSTOM_SOURCE_ID = uuid.UUID("616ba093-306c-45fc-8f41-0b98c8cccb46")


class Api36TraditionSpiritCategoriesE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_api36_and_digest_binds_full_five_field_authority(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "tradition-spirit-categories"', source)
        self.assertIn('if api != "36"', source)
        self.assertIn('"controlCount": len(CONTROLS)', source)
        self.assertIn('"fiveFieldLocalRevisions"', source)
        self.assertIn('"customOverlayAndFieldRevisionDriftFailClosedBySourceContract"', source)
        self.assertIn('device.shell("am", "force-stop"', source)
        for digest in (
            "spiritCategoryPageSha256",
            "spiritCategoryContractSha256",
            "spiritCategoryRulesSha256",
            "sourceResolverSha256",
            "traditionsCatalogSha256",
            "creationFixtureSha256",
            "careerFixtureSha256",
        ):
            self.assertIn(f'"{digest}"', source)
        for category in CATEGORIES:
            self.assertIn(f'f"tradition-spirit-{{category}}-value"', source)
        self.assertNotIn('"tablet"', source)

    def test_controls_and_paired_fixtures_are_exact_custom_mag_states(self) -> None:
        module = ast.parse(DRIVER.read_text(encoding="utf-8"))
        assignment = next(
            node for node in module.body
            if isinstance(node, ast.Assign)
            and any(isinstance(target, ast.Name) and target.id == "CATEGORIES" for target in node.targets)
        )
        self.assertEqual(CATEGORIES, tuple(ast.literal_eval(assignment.value)))
        controls_assignment = next(
            node for node in module.body
            if isinstance(node, ast.Assign)
            and any(isinstance(target, ast.Name) and target.id == "CONTROLS" for target in node.targets)
        )
        self.assertEqual(CONTROLS, tuple(ast.literal_eval(controls_assignment.value)))

        for path, created in ((CREATION, "False"), (CAREER, "True")):
            root = ET.parse(path).getroot()
            tradition = root.find("tradition")
            self.assertIsNotNone(tradition)
            self.assertEqual(created, root.findtext("created"))
            self.assertEqual("True", root.findtext("magenabled"))
            self.assertEqual("False", root.findtext("resenabled"))
            self.assertEqual("MAG", tradition.findtext("traditiontype"))
            self.assertEqual(CUSTOM_SOURCE_ID, uuid.UUID(tradition.findtext("sourceid", default="")))
            self.assertNotEqual(0, uuid.UUID(tradition.findtext("guid", default="")).int)
            self.assertEqual(
                {"Spirit of Fire", "Spirit of Air"},
                {
                    improvement.findtext("improvedname", default="")
                    for improvement in root.findall("./improvements/improvement")
                    if improvement.findtext("improvementttype") == "LimitSpiritCategory"
                    and improvement.findtext("enabled") in {"1", "True"}
                },
            )
            for category in CATEGORIES:
                self.assertIsNotNone(tradition.find(f"spirit{category}"))


if __name__ == "__main__":
    unittest.main()
