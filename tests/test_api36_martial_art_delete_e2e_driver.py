import ast
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
DRIVER = ROOT / "tests" / "run_api36_martial_art_delete_e2e.py"


class MartialArtDeleteDriverTests(unittest.TestCase):
    def test_driver_is_paired_phone_api36_arm64_package_and_digest_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        for token in (
            '"CharacterCreate.cmdDeleteMartialArt"',
            '"CharacterCareer.cmdDeleteMartialArt"',
            'PACKAGE = "com.myexternalbrain.chummer"',
            'ABI = "arm64-v8a"',
            'if api != "36"',
            'if abi != "arm64-v8a"',
            '"profile": "phone"',
            '"journey": "martial-art-delete"',
            '"creationCancelNoOp": "pass"',
            '"creationParentCascadeDeleted": "pass"',
            '"careerParentScopedTechniqueDeleted": "pass"',
            '"creationFixtureSha256"',
            '"careerFixtureSha256"',
            '"presenterPersistenceSha256"',
            '"workspaceStoreSha256"',
        ):
            self.assertIn(token, source)

    def test_receipt_waits_for_cancel_confirm_same_session_and_restart_proof(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        receipt = source.index("receipt = {")
        self.assertGreater(receipt, source.index('device.tap("Cancel")'))
        self.assertGreater(receipt, source.index('device.tap("Delete")'))
        self.assertGreater(receipt, source.index('device.shell("am", "force-stop", PACKAGE)'))
        self.assertGreater(receipt, source.index("assert_target_absent("))

    def test_fixtures_pin_cascade_parent_scope_sources_and_zero_refund_values(self) -> None:
        creation = ET.parse(ROOT / "tests/fixtures/creation-martial-art-delete-e2e.chum5").getroot()
        career = ET.parse(ROOT / "tests/fixtures/career-martial-art-delete-e2e.chum5").getroot()
        self.assertEqual("False", creation.findtext("created"))
        self.assertEqual("True", career.findtext("created"))
        self.assertEqual(2, len(creation.findall("./martialarts/martialart[1]/martialarttechniques/martialarttechnique")))
        self.assertEqual(
            ["Disarm", "Disarm"],
            [
                technique.findtext("name")
                for technique in career.findall("./martialarts/martialart/martialarttechniques/martialarttechnique")[:2]
            ],
        )
        self.assertEqual("29", creation.findtext("karma"))
        self.assertEqual("2345.67", creation.findtext("nuyen"))
        self.assertEqual("37", career.findtext("karma"))
        self.assertEqual("8765.43", career.findtext("nuyen"))
        self.assertTrue(any(
            improvement.findtext("improvementsource") == "Quality"
            and improvement.findtext("sourcename") == "a4111111-a411-a411-a411-a41111111111"
            for improvement in creation.findall("./improvements/improvement")
        ))


if __name__ == "__main__":
    unittest.main()
