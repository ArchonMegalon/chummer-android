"""Static wiring guards, not Android navigation or runtime-authority evidence."""

import unittest
from pathlib import Path


NATIVE = Path(__file__).resolve().parents[1] / "src" / "Chummer.Android" / "Native"


class FinalizationOwnerNavigationSourceContractTests(unittest.TestCase):
    def finalization_handler(self) -> str:
        source = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")
        return source.split('review.AutomationId = "creation-finalization-open-review";', 1)[1].split(
            "_body.Add(review);", 1
        )[0]

    def test_review_await_keeps_original_appearance_and_checks_issued_review_before_navigation(self):
        source = self.finalization_handler()
        capture = source.index("long originalAppearance = _creationDashboardAppearanceGeneration;")
        invocation = source.index("await Coordinator.ReviewCreationFinalizationAsync(authority.Value.Binding)")
        live_review = source.index("!Coordinator.IsCreationFinalizationReviewCurrent(result.Value)")
        navigation = source.index("await Navigation.PushAsync(new CreationFinalizationPage")
        self.assertLess(capture, invocation)
        self.assertLess(invocation, live_review)
        self.assertLess(live_review, navigation)
        self.assertIn("return;", source[live_review:navigation])

    def test_stale_worker_failure_is_contained_before_shared_navigation_error_handler(self):
        source = self.finalization_handler()
        rejection = source.split("catch (Exception exception) when (exception is not OutOfMemoryException)", 1)[1]
        rejection = rejection.split("throw;", 1)[0]
        self.assertIn("originalAppearance != _creationDashboardAppearanceGeneration", rejection)
        self.assertIn("!IsCurrentCreationDashboardPage()", rejection)
        self.assertIn("return;", rejection)

    def test_core_review_and_commit_dispatch_remain_whole_synchronous_worker_calls(self):
        source = (NATIVE / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        self.assertIn("=> Task.Run(() => ReviewCreationFinalization(binding), cancellationToken);", source)
        dispatch = source.split("result = await Task.Run(() =>", 1)[1].split("cancellationToken);", 1)[0]
        self.assertIn("_ownerBoundFinalizationService?.Confirm(owner, command)", dispatch)
        self.assertIn("original.DisplayOwnerContext", dispatch)
        self.assertNotIn("await", dispatch)


if __name__ == "__main__":
    unittest.main()
