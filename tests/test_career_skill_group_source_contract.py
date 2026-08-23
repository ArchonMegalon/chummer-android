from pathlib import Path
import unittest


REPO = Path(__file__).resolve().parents[1]


class CareerSkillGroupSourceContractTests(unittest.TestCase):
    def test_phone_page_binds_quote_confirmation_and_exact_expense_identity(self) -> None:
        page = (
            REPO / "src/Chummer.Android/Native/CareerSkillGroupAdvancePage.cs"
        ).read_text(encoding="utf-8")
        for automation_id in (
            "career-skill-group-page",
            "career-skill-group-picker",
            "career-skill-group-rating",
            "career-skill-group-cost",
            "career-skill-group-blocker",
            "career-skill-group-advance",
        ):
            self.assertIn(f'"{automation_id}"', page)
        self.assertIn("CharacterCareerSkillGroupAdvanceRules.IsCoherent", page)
        self.assertIn("selected.RuleDigest", page)
        self.assertIn("Confirmed: true", page)
        self.assertIn("ExpenseId: Guid.NewGuid()", page)
        self.assertIn("ExpenseDateLocal: DateTime.Now", page)
        self.assertIn('"Advance group"', page)
        self.assertIn('"Cancel"', page)
        self.assertIn("CharacterCareerSkillGroupAdvanceBlocker.Broken", page)
        self.assertIn("CharacterCareerSkillGroupAdvanceBlocker.Disabled", page)

    def test_coordinator_requires_cas_then_atomic_durable_save(self) -> None:
        coordinator = (
            REPO / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("PrepareCareerSkillGroupAdvanceAsync", coordinator)
        self.assertIn("ApplyCareerSkillGroupAdvanceAsync", coordinator)
        self.assertIn("State.ContentRevision == request.ExpectedContentRevision + 1", coordinator)
        self.assertIn("State.SavedRevision == appliedContentRevision", coordinator)
        self.assertIn("!State.IsDirty", coordinator)
        self.assertIn("TryRefreshWorkspaceAuthorityAsync", coordinator)
        self.assertIn("authority.Matches(State)", coordinator)

    def test_build_route_is_career_only_and_tablet_is_not_claimed(self) -> None:
        build = (REPO / "src/Chummer.Android/Native/BuildPage.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn('automationId: "build-career-skill-group"', build)
        self.assertIn("PrepareCareerSkillGroupAdvanceAsync", build)
        self.assertIn("new CareerSkillGroupAdvancePage", build)
        page = (
            REPO / "src/Chummer.Android/Native/CareerSkillGroupAdvancePage.cs"
        ).read_text(encoding="utf-8")
        self.assertNotIn("tablet", page.lower())


if __name__ == "__main__":
    unittest.main()
