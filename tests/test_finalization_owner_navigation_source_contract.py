"""Static wiring guards, not Android navigation or runtime-authority evidence."""

import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


NATIVE = Path(__file__).resolve().parents[1] / "src" / "Chummer.Android" / "Native"


class FinalizationOwnerNavigationSourceContractTests(unittest.TestCase):
    def test_starting_cash_title_and_help_exist_in_each_phone_language(self):
        resources = NATIVE.parent / "Resources" / "Localization"
        for suffix, title in (("", "Starting cash"), (".de", "Startgeld"), (".es", "Dinero inicial")):
            root = ET.parse(resources / f"CreationAllocationStrings{suffix}.resx").getroot()
            values = {entry.attrib["name"]: entry.findtext("value") for entry in root.findall("data")}
            self.assertEqual(title, values["Finalization.StartingCashTitle"])
            self.assertTrue(values["Finalization.StartingCashHelp"])

    def finalization_handler(self) -> str:
        source = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")
        return source.split('review.AutomationId = "creation-finalization-open-review";', 1)[1].split(
            "_body.Add(review);", 1
        )[0]

    def test_dashboard_opens_only_the_current_explicit_cash_choice(self):
        source = self.finalization_handler()
        self.assertIn("!IsCurrentCreationDashboardPage()", source)
        current = source.index("!Coordinator.IsCreationFinalizationStateCurrent(authority.Value)")
        navigation = source.index("await Navigation.PushAsync(new CreationStartingCashPage")
        self.assertLess(current, navigation)
        self.assertNotIn("ReviewCreationFinalizationAsync", source)

    def cash_page(self) -> str:
        return (NATIVE / "CreationFinalizationPage.cs").read_text(encoding="utf-8").split(
            "public sealed class CreationFinalizationPage", 1
        )[0]

    def test_review_await_retains_input_and_checks_current_page_before_navigation(self):
        source = self.cash_page()
        capture = source.index("long version = _inputVersion;")
        invocation = source.index("await Coordinator.ReviewCreationFinalizationAsync")
        live_review = source.index("Coordinator.IsCreationFinalizationReviewCurrent(result.Value)")
        navigation = source.index("await Navigation.PushAsync(new CreationFinalizationPage")
        self.assertLess(capture, invocation)
        self.assertLess(invocation, live_review)
        self.assertLess(live_review, navigation)
        self.assertIn("if (!Current() || version != _inputVersion) return;", source[invocation:live_review])
        self.assertIn("new CharacterCreationStartingCashChoice(source.AuthorityDigest, dice)", source)
        self.assertIn("private string _roll = string.Empty;", source)

    def test_stale_worker_failure_is_contained_before_shared_navigation_error_handler(self):
        source = self.cash_page()
        rejection = source.split("catch (Exception exception) when (exception is not OutOfMemoryException)", 1)[1]
        rejection = rejection.split("throw;", 1)[0]
        self.assertIn("!Current() || version != _inputVersion", rejection)
        self.assertIn("return;", rejection)

    def test_core_review_and_commit_dispatch_remain_whole_synchronous_worker_calls(self):
        source = (NATIVE / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        self.assertIn("=> Task.Run(() => ReviewCreationFinalization(binding), cancellationToken);", source)
        self.assertIn("=> Task.Run(() => ReviewCreationFinalization(binding, startingCash), cancellationToken);", source)
        self.assertIn("StartingCash = review.Plan.StartingCash", source)
        dispatch = source.split("result = await Task.Run(() =>", 1)[1].split("cancellationToken);", 1)[0]
        self.assertIn("_ownerBoundFinalizationService?.Confirm(owner, command)", dispatch)
        self.assertIn("original.DisplayOwnerContext", dispatch)
        self.assertNotIn("await", dispatch)


if __name__ == "__main__":
    unittest.main()
