from pathlib import Path
import unittest


REPO = Path(__file__).resolve().parents[1]


class CareerCalendarSourceContractTests(unittest.TestCase):
    def test_phone_page_exposes_exact_crud_and_fail_closed_start_shift(self) -> None:
        page = (
            REPO / "src/Chummer.Android/Native/Sr5DowntimeCalendarWizardPage.cs"
        ).read_text(
            encoding="utf-8"
        )
        for automation_id in (
            "sr5-downtime-calendar-page",
            "sr5-downtime-calendar-binding",
            "sr5-downtime-calendar-operation",
            "sr5-downtime-calendar-week",
            "sr5-downtime-calendar-year",
            "sr5-downtime-calendar-iso-week",
            "sr5-downtime-calendar-notes",
            "sr5-downtime-calendar-notes-color",
            "sr5-downtime-calendar-review",
            "sr5-downtime-calendar-preview",
            "sr5-downtime-calendar-confirm",
            "sr5-downtime-calendar-apply",
            "sr5-downtime-calendar-status",
            "sr5-downtime-calendar-outcome-unknown",
            "sr5-downtime-calendar-receipt",
            "sr5-downtime-calendar-clear-applied",
        ):
            self.assertIn(f'"{automation_id}"', page)
        self.assertIn("_session.TryPreviewAdd(Guid.NewGuid()", page)
        self.assertIn("_session.TryPreviewEdit", page)
        self.assertIn("_session.TryPreviewDelete", page)
        self.assertIn("_session.TryConfirm", page)
        self.assertIn("_journalStore.TryWriteReview", page)
        self.assertIn("_journalStore.TryBeginApplying", page)
        self.assertIn("_journalStore.TryComplete", page)
        self.assertIn("_operation.SelectedIndex == 0", page)
        self.assertIn("(_load?.Editor?.Weeks.Count ?? 0) == 0", page)
        self.assertIn("_week.IsVisible = _year.IsVisible", page)

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
        self.assertIn("new Sr5DowntimeCalendarWizardPage(Coordinator)", build)
        page = (
            REPO / "src/Chummer.Android/Native/Sr5DowntimeCalendarWizardPage.cs"
        ).read_text(
            encoding="utf-8"
        )
        authority = (
            REPO
            / "src/Chummer.Android/Native/RunnerSessionSr5DowntimeCalendarAuthority.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("new RunnerSessionSr5DowntimeCalendarAuthority(coordinator)", page)
        self.assertIn("PrepareCareerCalendarEditAsync", authority)
        self.assertIn("CaptureSr5CareerWizardWorkspaceAuthorityAsync", authority)
        self.assertNotIn("tablet", page.lower())


if __name__ == "__main__":
    unittest.main()
