from pathlib import Path
import unittest


REPO = Path(__file__).resolve().parents[1]


class CareerManualKarmaSourceContractTests(unittest.TestCase):
    def test_phone_route_exposes_exact_fields_and_revision_bound_save(self) -> None:
        build = (REPO / "src/Chummer.Android/Native/BuildPage.cs").read_text(encoding="utf-8")
        page = (REPO / "src/Chummer.Android/Native/CareerManualKarmaPage.cs").read_text(encoding="utf-8")
        coordinator = (REPO / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        self.assertIn('automationId: "build-career-manual-karma"', build)
        self.assertIn("new CareerManualKarmaPage", build)
        for marker in (
            'AutomationId = "career-manual-karma-page"',
            'AutomationId = "career-manual-karma-amount"',
            '"career-manual-karma-reason"',
            'AutomationId = "career-manual-karma-date"',
            'AutomationId = "career-manual-karma-time"',
            '"career-manual-karma-refund"',
            '"career-manual-karma-exchange"',
            '"career-manual-karma-force-career-visible"',
            'AutomationId = "career-manual-karma-gain"',
            'AutomationId = "career-manual-karma-spend"',
            "_editor.ContentRevision",
        ):
            self.assertIn(marker, page)
        for marker in (
            "PrepareCareerManualKarmaEditAsync",
            "ApplyCareerManualKarmaEditAsync",
            "ExpectedContentRevision",
            "_presenter.SaveAsync",
        ):
            self.assertIn(marker, coordinator)


if __name__ == "__main__":
    unittest.main()
