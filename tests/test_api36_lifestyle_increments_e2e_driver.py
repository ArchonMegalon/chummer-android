from __future__ import annotations

import hashlib
from pathlib import Path
import unittest


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_lifestyle_increments_e2e.py"


class Api36LifestyleIncrementDriverTests(unittest.TestCase):
    def test_driver_is_phone_api36_restart_and_transaction_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        for marker in (
            '"CharacterCreate.nudLifestyleMonths"',
            '"CharacterCareer.cmdIncreaseLifestyleMonths"',
            '"CharacterCareer.cmdDecreaseLifestyleMonths"',
            'api != "36"',
            '"profile": "phone"',
            '"journey": "lifestyle-increments"',
            'f"lifestyle-increments-value-{token}"',
            'f"lifestyle-increments-increase-{increase_token}"',
            'f"lifestyle-increments-decrease-{decrease_token}"',
            'device.shell("am", "force-stop"',
            '"careerPurchaseExpenseAndUndo": "pass"',
            '"careerDecreaseZeroExpenseAndNegativeLegacyBound": "pass"',
            '"lifestyleIncrementRulesSha256"',
            '"presenterPersistenceSha256"',
            '"workspaceStoreSha256"',
        ):
            self.assertIn(marker, source)

    def test_receipt_binds_driver_apk_fixtures_and_full_source_graph(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        self.assertIn('"driverSha256": shared.sha256(driver)', source)
        self.assertIn('"apkSha256": shared.sha256(args.apk.resolve())', source)
        self.assertIn('"creationFixtureSha256": shared.sha256(creation_fixture)', source)
        self.assertIn('"careerFixtureSha256": shared.sha256(career_fixture)', source)
        self.assertEqual(64, len(hashlib.sha256(source.encode("utf-8")).hexdigest()))


if __name__ == "__main__":
    unittest.main()
