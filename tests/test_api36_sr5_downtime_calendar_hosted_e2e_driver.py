import ast
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]
DRIVER = ROOT / "tests/run_api36_sr5_downtime_calendar_hosted_e2e.py"


class Api36Sr5DowntimeCalendarHostedDriverTests(unittest.TestCase):
    def test_hosted_driver_is_phone_api36_only_and_uses_typed_wizard(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        for marker in (
            'RECEIPT_SCHEMA = "chummer.android.editing-e2e/v1"',
            'JOURNEY = "sr5-downtime-calendar"',
            'requires API 36',
            'requires x86_64',
            "run_api36_sr5_downtime_calendar_e2e",
            "prove_downtime",
            '"profile": "phone"',
            '"executionStatus": "pass"',
            '"atomicApplyAndReceipt": "pass"',
        ):
            self.assertIn(marker, source)
        self.assertNotIn("tablet", source.lower())
        self.assertNotIn("full-editing", source)
        self.assertNotIn("--allow-destructive-disposable-device", source)

    def test_hosted_driver_requires_explicit_receipt_and_committed_fixture(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        for marker in (
            'parser.add_argument("--receipt", type=Path, required=True)',
            'parser.add_argument("--workspace-root", type=Path)',
            'parser.add_argument("--runner", type=Path, default=DEFAULT_RUNNER)',
            'runner != DEFAULT_FIXTURE.parent / str(fixture["runnerFixture"])',
            'device.publish_document_for_documents_ui(',
            '"verifiedRemoteRunnerSha256": provider_registration["sha256"]',
            '"documentsUiProviderRegistration": provider_registration',
        ):
            self.assertIn(marker, source)
        self.assertNotIn("device.push_verified(", source)


if __name__ == "__main__":
    unittest.main()
