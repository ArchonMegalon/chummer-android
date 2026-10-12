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
            self.assertTrue(values["Finalization.StartingLifestyle"])
            self.assertTrue(values["Karma.DiceTotalHelp"])
            self.assertTrue(values["Finalization.InvalidDiceTotal"])
            for key in ("Title", "Review", "ReviewHelp", "StaleReview", "EnterCareer", "After", "KarmaRemaining",
                        "NuyenRemaining", "Change", "ConfirmHelp", "Saving", "Confirm", "Mode", "CareerTransition",
                        "SavedTitle", "ReceiptAccount", "CareerReady", "BuildMethod", "Career", "Creation", "Reopened",
                        "ReopenRequired", "OpenCareer", "ViewConfirmation", "Method.Priority", "Method.SumToTen", "Method.Karma", "Method.LifeModules",
                        "CashLifestyle", "CashDiceTotal"):
                self.assertTrue(values[f"Finalization.{key}"], (suffix, key))

    def finalization_handler(self) -> str:
        source = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")
        return source.split('review.AutomationId = "creation-finalization-open-review";', 1)[1].split(
            "_body.Add(review);", 1
        )[0]

    def test_dashboard_opens_only_the_current_explicit_cash_choice(self):
        handler = self.finalization_handler()
        self.assertIn("RunCreationNavigationAsync(route.Open, pressGeneration)", handler)
        dashboard = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")
        source = dashboard.split("private CreationBudgetRoute CreationFinalizationReviewRoute()", 1)[1].split(
            "private void AddCreationFinalizationStatus", 1)[0]
        self.assertIn("generation == _dossierRenderGeneration", source)
        self.assertIn("&& IsCurrentCreationDashboardPage()", source)
        current = source.index("&& Coordinator.IsCreationFinalizationStateCurrent(authority!.Value!)")
        navigation = source.index("Navigation.PushAsync(new CreationStartingCashPage")
        self.assertLess(current, navigation)
        self.assertNotIn("ReviewCreationFinalizationAsync", source)

    def test_cash_selector_uses_only_issued_options_and_invalidates_old_input(self):
        source = self.cash_page()
        self.assertIn("_authority.StartingCashSources?.ToArray()", source)
        self.assertIn("if (sources.Length > 1)", source)
        selection = source.split("picker.SelectedIndexChanged +=", 1)[1].split("_body.Add(picker);", 1)[0]
        self.assertIn("!Current()", selection)
        self.assertIn("_sourceDigest = selected;", selection)
        self.assertIn('_roll = string.Empty;', selection)
        self.assertIn("_inputVersion++;", selection)
        self.assertIn("Refresh();", selection)

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

    def test_editing_cash_retires_old_feedback_without_refresh_or_review(self):
        handler = self.cash_page().split("input.TextChanged +=", 1)[1].split("preview.Clicked +=", 1)[0]
        self.assertLess(handler.index("if (!Current()) return;"), handler.index("_blockers = [];"))
        self.assertIn("feedback.IsVisible = false;", handler)
        self.assertIn("_inputVersion++;", handler)
        self.assertIn("preview.IsEnabled = TryRoll(out _);", handler)
        self.assertNotIn("Refresh();", handler)
        self.assertNotIn("ReviewCreationFinalizationAsync", handler)
        self.assertNotIn("Navigation", handler)

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
        confirm = source.split("ConfirmCreationFinalizationAsync(", 1)[1].split("\n    public ", 1)[0]
        dispatch = confirm.split("result = await Task.Run(() =>", 1)[1].split("cancellationToken);", 1)[0]
        self.assertIn("_ownerBoundFinalizationService?.Confirm(owner, command)", dispatch)
        self.assertIn("original.DisplayOwnerContext", dispatch)
        self.assertNotIn("await", dispatch)

    def test_successful_finalization_retires_wizard_only_after_current_career_restore(self):
        source = (NATIVE / "CreationFinalizationPage.cs").read_text(encoding="utf-8")
        continuation = source.split("private async Task OpenCompletionAsync", 1)[1].split("// Display only:", 1)[0]
        self.assertIn("navigation.NavigationStack.SequenceEqual(originalStack)", continuation)
        self.assertIn("Coordinator.CanDisplayCreationFinalizationReceipt(receipt)", continuation)
        self.assertLess(continuation.index("_completion.Blockers.Count == 0"), continuation.index("PopToRootAsync"))
        self.assertLess(continuation.index("Coordinator.IsCreationFinalizationReceiptCurrent(receipt)"), continuation.index("PopToRootAsync"))
        self.assertIn("CreationFinalizationReceiptPage", continuation)
        self.assertNotIn("ConfirmCreationFinalizationAsync", continuation)

    def test_completion_is_retained_before_page_checks_and_never_reconfirmed(self):
        source = (NATIVE / "CreationFinalizationPage.cs").read_text(encoding="utf-8")
        confirm = source.split("private async Task ConfirmAsync()", 1)[1].split("private bool IsCurrentPage", 1)[0]
        self.assertIn("_completion is not null", confirm)
        self.assertLess(confirm.index("_completion = result"), confirm.index("if (!IsCurrentPage(originalAppearance)"))
        self.assertIn("ReferenceEquals(confirm.Parent, _body)", source)


if __name__ == "__main__":
    unittest.main()
