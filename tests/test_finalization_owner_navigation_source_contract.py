"""Static wiring guards, not Android navigation or runtime-authority evidence."""

import re
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


NATIVE = Path(__file__).resolve().parents[1] / "src" / "Chummer.Android" / "Native"


class FinalizationOwnerNavigationSourceContractTests(unittest.TestCase):
    def test_completion_starter_fills_only_missing_choices_and_keeps_current_core_review(self):
        source = (NATIVE / "CreationCompletionPage.cs").read_text(encoding="utf-8")
        self.assertIn("new CompletionAdditivePermissions([], [])", source)
        self.assertNotIn("_allowAttributes", source)
        self.assertNotIn("_allowSkills", source)
        self.assertNotIn("creation-completion-allow-", source)
        self.assertNotIn("CheckBox", source)
        self.assertIn('"completion.remaining.attribute-choice-intent" => Copy("AttributeChoiceIntent"', source)
        coordinator = (NATIVE / "RunnerSessionCoordinator.CreationCompletion.cs").read_text(encoding="utf-8")
        proposal = coordinator.split("BuildCreationCompletionProposalAsync(", 1)[1].split("ReviewCreationCompletionAsync(", 1)[0]
        self.assertIn("CompletionRecommendationBuilder.BuildMissingOnly(inputs, intent, cancellationToken)", proposal)
        self.assertIn("CompletionDisplayCurrent(issued.Display)", proposal)
        self.assertIn("intent.OwnerContext != issued.Display.DisplayOwnerContext", proposal)
        self.assertIn("intent.InputsSnapshotDigest != issued.SnapshotDigest", proposal)
        self.assertIn("inputs.SnapshotDigest != issued.SnapshotDigest", proposal)
        self.assertIn("LoadCreationFinalizationInBackground(", source)
        self.assertNotIn("IsCreationFinalizationStateCurrent", source)
        self.assertIn('_roll = string.Empty;', source)
        self.assertIn("new(source.AuthorityDigest, dice)", source)
        self.assertIn("Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } review, Blockers.Count: 0", source)
        self.assertIn("Coordinator.IsCreationCompletionReviewCurrent(review)", source)
        cash = source.split("lifestyle.SelectedIndexChanged +=", 1)[1].split("_body.Add(lifestyle)", 1)[0]
        self.assertIn("InvalidateProposal();", cash)
        self.assertNotIn("Random", source)

    def test_completion_retains_known_receipt_and_only_recovers_returned_unknown_outcomes(self):
        source = (NATIVE / "CreationCompletionPage.cs").read_text(encoding="utf-8")
        apply = source.split("private async Task ApplyAsync(", 1)[1].split("private void RenderSaved", 1)[0]
        self.assertIn("recoveryOnly != _applyStarted", apply)
        self.assertIn("recoveryOnly && !_outcomeLookupAllowed", apply)
        self.assertIn('_outcomeLookupAllowed = _completion.Outcome == "outcome-unknown"', apply)
        self.assertLess(apply.index("_completion = await Coordinator.ApplyCreationCompletionAsync"),
                        apply.index("if (!IsCurrentPage(appearance)"))
        failure = apply.split("catch (Exception error)", 1)[1]
        self.assertIn("if (_completion?.Value is null)", failure)
        self.assertIn("_outcomeLookupAllowed = false;", failure)
        self.assertIn("AddCancel(appearance, render, afterRejection: true)", source)
        cancel = source.split("private void AddCancel(", 1)[1].split("private Button AddAction", 1)[0]
        self.assertIn("|| _leaving", cancel)
        self.assertIn("try { await Navigation.PopAsync(); }", cancel)
        self.assertIn("catch (Exception error)", cancel)

    def test_completion_copy_is_localized_without_identifier_fallbacks(self):
        source = (NATIVE / "CreationCompletionPage.cs").read_text(encoding="utf-8")
        dashboard = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")
        keys = {"Completion." + key for key in re.findall(r'\bCopy\("([^"]+)"', source + dashboard)}
        keys.add("Completion.Rating")
        # BuildPage contains other copy helpers; only this page and its named entry use this prefix.
        keys = {key for key in keys if key != "Completion.Receipt"}
        resources = NATIVE.parent / "Resources" / "Localization"
        for suffix in ("", ".de", ".es"):
            root = ET.parse(resources / f"CreationFlowStrings{suffix}.resx").getroot()
            values = {entry.attrib["name"]: entry.findtext("value") for entry in root.findall("data")}
            for key in keys:
                self.assertTrue(values.get(key), (suffix, key))
        labels = source.split("private string SuggestionLabel", 1)[1].split("private static string RemainingTask", 1)[0]
        self.assertNotIn(": choice.SourceId", labels)
        self.assertIn("SkillCatalogStrings.SkillName", labels)

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
                        "CashLifestyle", "CashDiceTotal", "ShowChanges", "HideChanges", "CarryoverHelp"):
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
        self.assertIn("ReferenceEquals(confirm.Parent, _footer)", source)


if __name__ == "__main__":
    unittest.main()
