import ast
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]
DRIVER = ROOT / "tests" / "run_api36_martial_art_notes_e2e.py"


class MartialArtNotesDriverTests(unittest.TestCase):
    def test_driver_is_paired_phone_api36_arm64_package_and_digest_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        for token in (
            '"CharacterCreate.tsMartialArtsNotes"',
            '"CharacterCareer.tsMartialArtsNotes"',
            'PACKAGE = "com.myexternalbrain.chummer"',
            'ABI = "arm64-v8a"',
            'if api != "36"',
            'if abi != "arm64-v8a"',
            '"profile": "phone"',
            '"journey": "martial-art-notes"',
            '"creationMartialArtNotesEdited": "pass"',
            '"careerParentScopedTechniqueNotesEdited": "pass"',
            '"creationFixtureSha256"',
            '"careerFixtureSha256"',
            '"presenterPersistenceSha256"',
            '"workspaceStoreSha256"',
        ):
            self.assertIn(token, source)

    def test_receipt_waits_for_both_mode_restart_readbacks(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        receipt = source.index("receipt = {")
        self.assertGreater(receipt, source.index('mode="creation"'))
        self.assertGreater(receipt, source.index('mode="career"'))
        self.assertGreater(receipt, source.index('device.shell("am", "force-stop", PACKAGE)'))
        self.assertGreater(receipt, source.index("assert_workspace("))


if __name__ == "__main__":
    unittest.main()
