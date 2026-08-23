from pathlib import Path
import unittest


REPO = Path(__file__).resolve().parents[1]


class CareerCalendarSourceContractTests(unittest.TestCase):
    def test_phone_page_exposes_exact_crud_and_fail_closed_start_shift(self) -> None:
        page = (REPO / "src/Chummer.Android/Native/CareerCalendarPage.cs").read_text(
            encoding="utf-8"
        )
        for automation_id in (
            "career-calendar-page",
            "career-calendar-week-picker",
            "career-calendar-first-year",
            "career-calendar-first-week",
            "career-calendar-add",
            "career-calendar-notes",
            "career-calendar-notes-color",
            "career-calendar-save",
            "career-calendar-delete",
            "career-calendar-change-start-disabled",
            "career-calendar-change-start-blocker",
        ):
            self.assertIn(f'"{automation_id}"', page)
        self.assertIn("CharacterCareerCalendarRules.TryPlanAdd", page)
        self.assertIn("CharacterCareerCalendarRules.TryEdit", page)
        self.assertIn("CharacterCareerCalendarRules.CanDelete", page)
        self.assertIn("new(Guid.NewGuid())", page)
        self.assertIn("Confirmed: true", page)
        self.assertIn("changeStart.IsEnabled = false", page)

    def test_coordinator_requires_exact_revision_then_atomic_durable_save(self) -> None:
        coordinator = (
            REPO / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("PrepareCareerCalendarEditAsync", coordinator)
        self.assertIn("ApplyCareerCalendarAddAsync", coordinator)
        self.assertIn("ApplyCareerCalendarEditAsync", coordinator)
        self.assertIn("ApplyCareerCalendarDeleteAsync", coordinator)
        self.assertIn("State.ContentRevision == expectedContentRevision + 1", coordinator)
        self.assertIn("State.SavedRevision == appliedContentRevision", coordinator)
        self.assertIn("!State.IsDirty", coordinator)
        self.assertIn("TryRefreshWorkspaceAuthorityAsync", coordinator)

    def test_build_route_is_career_only_and_tablet_is_not_claimed(self) -> None:
        build = (REPO / "src/Chummer.Android/Native/BuildPage.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn('automationId: "build-career-calendar"', build)
        self.assertIn("PrepareCareerCalendarEditAsync", build)
        self.assertIn("new CareerCalendarPage", build)
        page = (REPO / "src/Chummer.Android/Native/CareerCalendarPage.cs").read_text(
            encoding="utf-8"
        )
        self.assertNotIn("tablet", page.lower())


if __name__ == "__main__":
    unittest.main()
