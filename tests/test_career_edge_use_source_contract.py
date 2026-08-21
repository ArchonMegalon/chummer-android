from pathlib import Path
import unittest


REPO = Path(__file__).resolve().parents[1]


class CareerEdgeUseSourceContractTests(unittest.TestCase):
    def test_phone_route_is_revision_bound_and_saves_through_presenter(self) -> None:
        build = (REPO / "src/Chummer.Android/Native/BuildPage.cs").read_text(encoding="utf-8")
        page = (REPO / "src/Chummer.Android/Native/CareerEdgeUsePage.cs").read_text(encoding="utf-8")
        coordinator = (REPO / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        self.assertIn('automationId: "build-career-edge-use"', build)
        self.assertIn("new CareerEdgeUsePage", build)
        for marker in (
            'AutomationId = "career-edge-use-page"',
            'AutomationId = "career-edge-use-spend"',
            'AutomationId = "career-edge-use-regain"',
            "_editor.ContentRevision",
            "CharacterCareerEdgeUseAction.Spend",
            "CharacterCareerEdgeUseAction.Regain",
        ):
            self.assertIn(marker, page)
        for marker in (
            "PrepareCareerEdgeUseEditAsync",
            "ApplyCareerEdgeUseEditAsync",
            "ExpectedContentRevision",
            "_presenter.SaveAsync",
        ):
            self.assertIn(marker, coordinator)


if __name__ == "__main__":
    unittest.main()
