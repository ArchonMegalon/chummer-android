from pathlib import Path
import unittest


REPO = Path(__file__).resolve().parents[1]


class GroupMembershipSourceContractTests(unittest.TestCase):
    def test_phone_route_is_revision_bound_and_saves_through_presenter(self) -> None:
        build = (REPO / "src/Chummer.Android/Native/BuildPage.cs").read_text(encoding="utf-8")
        page = (REPO / "src/Chummer.Android/Native/GroupMembershipPage.cs").read_text(encoding="utf-8")
        coordinator = (REPO / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs").read_text(encoding="utf-8")

        self.assertIn('automationId: "build-group-membership"', build)
        self.assertIn("new GroupMembershipPage", build)
        for marker in (
            'AutomationId = "group-membership-page"',
            'AutomationId = "group-membership-toggle"',
            'AutomationId = "group-membership-save"',
            "_editor.ContentRevision",
            "CharacterGroupMembershipState",
            "Spend & Save",
        ):
            self.assertIn(marker, page)
        for marker in (
            "PrepareGroupMembershipEditAsync",
            "ApplyGroupMembershipEditAsync",
            "ExpectedContentRevision",
            "_presenter.SaveAsync",
        ):
            self.assertIn(marker, coordinator)


if __name__ == "__main__":
    unittest.main()
