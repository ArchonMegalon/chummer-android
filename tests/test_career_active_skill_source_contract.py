from pathlib import Path
import unittest


REPO = Path(__file__).resolve().parents[1]


class CareerActiveSkillSourceContractTests(unittest.TestCase):
    def test_phone_page_binds_quote_confirmation_and_exact_expense_identity(self) -> None:
        page = (
            REPO / "src/Chummer.Android/Native/CareerActiveSkillAdvancePage.cs"
        ).read_text(encoding="utf-8")
        for automation_id in (
            "career-active-skill-page",
            "career-active-skill-picker",
            "career-active-skill-rating",
            "career-active-skill-cost",
            "career-active-skill-blocker",
            "career-active-skill-advance",
        ):
            self.assertIn(f'"{automation_id}"', page)
        self.assertIn("CharacterCareerActiveSkillAdvanceRules.IsCoherent", page)
        self.assertIn("selected.RuleDigest", page)
        self.assertIn("Confirmed: true", page)
        self.assertIn("ExpenseId: Guid.NewGuid()", page)
        self.assertIn("ExpenseDateLocal: DateTime.Now", page)
        self.assertIn('"Advance"', page)
        self.assertIn('"Cancel"', page)

    def test_coordinator_requires_cas_then_atomic_durable_save(self) -> None:
        coordinator = (
            REPO / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("PrepareCareerActiveSkillAdvanceAsync", coordinator)
        self.assertIn("ApplyCareerActiveSkillAdvanceAsync", coordinator)
        self.assertIn("State.ContentRevision == request.ExpectedContentRevision + 1", coordinator)
        self.assertIn("State.SavedRevision == appliedContentRevision", coordinator)
        self.assertIn("!State.IsDirty", coordinator)
        self.assertIn("TryRefreshWorkspaceAuthorityAsync", coordinator)
        self.assertIn("authority.Matches(State)", coordinator)

    def test_build_route_is_career_only_and_tablet_is_not_claimed(self) -> None:
        build = (REPO / "src/Chummer.Android/Native/BuildPage.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn('automationId: "build-career-active-skill"', build)
        self.assertIn("PrepareCareerActiveSkillAdvanceAsync", build)
        self.assertIn("new CareerActiveSkillAdvancePage", build)
        page = (
            REPO / "src/Chummer.Android/Native/CareerActiveSkillAdvancePage.cs"
        ).read_text(encoding="utf-8")
        self.assertNotIn("tablet", page.lower())


if __name__ == "__main__":
    unittest.main()
