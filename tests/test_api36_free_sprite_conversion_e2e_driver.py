import ast
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]
DRIVER = ROOT / "tests" / "run_api36_free_sprite_conversion_e2e.py"


class FreeSpriteConversionDriverTests(unittest.TestCase):
    def test_driver_is_phone_api36_arm64_and_digest_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        for token in (
            '"CharacterCreate.mnuSpecialConvertToFreeSprite"',
            '"CharacterCareer.mnuSpecialConvertToFreeSprite"',
            'PACKAGE = "com.myexternalbrain.chummer"',
            'ABI = "arm64-v8a"',
            'if api != "36"',
            'if abi != "arm64-v8a"',
            '"profile": "phone"',
            '"journey": "free-sprite-conversion"',
            '"creationExactConversion": "pass"',
            '"careerExactConversion": "pass"',
            '"creationFixtureSha256"',
            '"careerFixtureSha256"',
            '"presenterPersistenceSha256"',
            '"workspaceStoreSha256"',
        ):
            self.assertIn(token, source)

    def test_receipt_is_written_only_after_both_modes_and_restart_assertions(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        receipt = source.index("receipt = {")
        self.assertGreater(receipt, source.index('"creation",\n    )'))
        self.assertGreater(receipt, source.index('"career",\n    )'))
        self.assertGreater(receipt, source.index('device.shell("am", "force-stop", PACKAGE)'))
        self.assertGreater(receipt, source.index("assert_workspace(device, sentinel"))


if __name__ == "__main__":
    unittest.main()
