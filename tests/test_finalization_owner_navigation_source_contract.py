"""Static wiring guards, not Android navigation or runtime-authority evidence."""

import re
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


NATIVE = Path(__file__).resolve().parents[1] / "src" / "Chummer.Android" / "Native"


class FinalizationOwnerNavigationSourceContractTests(unittest.TestCase):
    def test_completion_magic_identity_picker_is_visible_guarded_and_keeps_original_choices(self):
        source = (NATIVE / "CreationCompletionPage.cs").read_text(encoding="utf-8")
        choices = source.split("private void RenderChoices(", 1)[1].split("private void InvalidateProposal()", 1)[0]
        for kind in ("tradition", "stream"):
            self.assertIn(f'"creation-completion-{kind}"', choices)
        picker = choices.split("private void RenderMagicIdentityChoice(", 1)[1]
        original = picker.split("if (original is not null)", 1)[1].split("var choices =", 1)[0]
        self.assertIn("MagicOptionLabel(original)", original)
        self.assertIn('automationId + "-retained"', original)
        self.assertIn("return;", original)
        self.assertNotIn("new Picker", original)
        self.assertIn("CompletionRecommendationBuilder.GetMagicIdentityChoices(_inputs!, kind)", picker)
        self.assertIn("if (preferred is null)", picker)
        self.assertIn("preferred = choices[0].Identity;", picker)
        self.assertIn("SelectedIndex = Array.FindIndex(choices, option => option.Identity == preferred)", picker)
        self.assertLess(picker.index("SelectedIndex ="), picker.index("picker.SelectedIndexChanged +="))
        handler = picker.split("picker.SelectedIndexChanged +=", 1)[1].split("_body.Add(NativeTheme.FieldLabel", 1)[0]
        self.assertIn("!Current(appearance, render) || _busy", handler)
        self.assertIn("picker.SelectedIndex >= choices.Length", handler)
        self.assertLess(handler.index("!Current(appearance, render)"), handler.index("select(choices["))
        self.assertIn("InvalidateProposal();", handler)
        invalidation = source.split("private void InvalidateProposal()", 1)[1].split("private async Task BuildAsync()", 1)[0]
        for retired in ("_inputVersion++;", "_proposal = null;", "_review = null;", '_roll = string.Empty;'):
            self.assertIn(retired, invalidation)
        self.assertNotIn("_preferredTradition =", invalidation)
        self.assertNotIn("_preferredStream =", invalidation)
        build = source.split("private async Task BuildAsync()", 1)[1].split("private void RenderProposal()", 1)[0]
        self.assertIn("PreferredTradition = inputs.Magic.Selections.Tradition is null ? _preferredTradition : null", build)
        self.assertIn("PreferredStream = inputs.Magic.Selections.Stream is null ? _preferredStream : null", build)
        self.assertNotIn("DesiredTotalMysticAdeptPowerPoints", source)

    def test_completion_magic_summary_uses_evaluated_new_identities_and_exact_catalog_names(self):
        source = (NATIVE / "CreationCompletionPage.cs").read_text(encoding="utf-8")
        proposal = source.split("private void RenderProposal()", 1)[1].split("private void RenderMagicProposal()", 1)[0]
        self.assertLess(proposal.index("RenderMagicProposal();"), proposal.index('Copy("Remaining"'))
        magic = source.split("private void RenderMagicProposal()", 1)[1].split("private void RenderResourceProposal()", 1)[0]
        self.assertIn("var original = inputs.Magic.Selections;", magic)
        self.assertIn("var proposed = plan.Selections.MagicSelections;", magic)
        self.assertIn("original.Tradition is null && proposed.Tradition", magic)
        self.assertIn("original.Stream is null && proposed.Stream", magic)
        self.assertIn("!original.Spells.Contains(identity)", magic)
        self.assertIn("!original.ComplexForms.Contains(identity)", magic)
        for kind in ("tradition", "stream", "spell", "complex-form"):
            self.assertIn(f'"creation-completion-proposed-{kind}"', magic)
        self.assertIn('AutomationId = "creation-completion-magic-changes"', magic)
        self.assertIn("options.Where(option => option.Identity == identity).Take(2)", magic)
        self.assertIn("MagicCatalogStrings.OptionName(identity.Kind, identity.SourceId, matches[0].Name)", magic)
        self.assertIn('Copy("MagicUnknown", "Unavailable magic choice")', magic)
        self.assertNotIn(": identity.SourceId", magic)
        self.assertNotIn("SourceNodeXml", magic)
        self.assertNotIn("ApplyCreationCompletionAsync", magic)

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

    def test_completion_starting_cash_read_retains_exact_proposal_owner_and_source_guards(self):
        source = (NATIVE / "RunnerSessionCoordinator.CreationCompletion.cs").read_text(encoding="utf-8")
        cash = source.split("LoadCreationCompletionStartingCashAsync(", 1)[1].split(
            "ReviewCreationCompletionAsync(", 1)[0]
        dispatch = cash.index("service.LoadStartingCash(owner, workspaceId, request, cancellationToken)")
        before, after = cash[:dispatch], cash[dispatch:]
        for guard in ("WithWorkspaceActivationGateAsync", "cancellationToken.ThrowIfCancellationRequested()",
                      "_completionProposals.TryGetValue(proposal, out var issued)",
                      "!CompletionDisplayCurrent(issued.Display)",
                      "issued.Display.DisplayOwnerContext is not { } owner",
                      "issued.Display.WorkspaceId is not { } workspaceId",
                      "FreezeCompletionValue(proposal)",
                      "OwnerContext = proposal.Intent.OwnerContext",
                      "frozen.ProposalDigest != issued.ProposalDigest",
                      "CompletionAssistantRules.ComputeProposalDigest(frozen) != issued.ProposalDigest",
                      "frozen.Intent.OwnerContext != owner", "frozen.Evaluation.Value is not { } plan",
                      "new CharacterCreationCompletionReviewRequest(frozen.Intent.InputsSnapshotDigest, plan.Selections)",
                      "ReadCreationAuthority(issued.Display"):
            self.assertIn(guard, before)
        self.assertIn("!CompletionDisplayCurrent(issued.Display)", after)
        self.assertIn("CompletionAssistantRules.ComputeProposalDigest(proposal) != issued.ProposalDigest", after)
        self.assertIn("result.Outcome != CharacterCreationFoundationOutcomes.Success", after)
        self.assertIn("!CharacterCreationKarmaFinalizationBudgetRules.IsValidStartingCashSource(source)", after)
        self.assertNotIn("new CharacterCreationStartingNuyenSource", cash)
        self.assertNotIn("service.Apply(", cash)
        self.assertNotIn("service.Review(", cash)
        self.assertNotIn("Random", cash)

    def test_completion_page_uses_current_proposal_cash_and_clears_stale_fallback_on_failure(self):
        source = (NATIVE / "CreationCompletionPage.cs").read_text(encoding="utf-8")
        build = source.split("private async Task BuildAsync()", 1)[1].split("private void RenderProposal()", 1)[0]
        invocation = build.index("await Coordinator.LoadCreationCompletionStartingCashAsync(proposal, ReadToken)")
        self.assertLess(build.index("if (!CurrentInput(appearance, version)) return;"), invocation)
        self.assertLess(build.index("_proposal = result.Value;"), invocation)
        before = build[:invocation]
        self.assertLess(before.index("var candidateCashSources = _cashSources;"), before.index("_cashSources = [];"))
        self.assertIn("_cashDigest = null;", before)
        after = build[invocation:]
        current = after.index("if (!CurrentInput(appearance, version) || !ReferenceEquals(_proposal, proposal)) return;")
        accepted = after.index("_cashDigest = source.AuthorityDigest;")
        self.assertLess(current, accepted)
        success = after.split("Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } source, Blockers.Count: 0", 1)[1]
        success = success.split("else", 1)[0]
        self.assertIn("_cashSources = candidateCashSources.Any(row => row.AuthorityDigest == source.AuthorityDigest)", success)
        self.assertIn("? candidateCashSources.Select(row => row.AuthorityDigest == source.AuthorityDigest ? source : row).ToArray()", success)
        self.assertIn(": [source];", success)
        self.assertIn("_cashDigest = source.AuthorityDigest;", success)
        self.assertIn("_cashSources = [];", after[accepted:])
        self.assertIn("_cashDigest = null;", after[accepted:])
        self.assertIn(".. cash.Blockers", after[accepted:])
        self.assertNotIn("_cashSources =", build.split("catch (OperationCanceledException)", 1)[1])
        self.assertNotIn("_cashDigest =", build.split("catch (OperationCanceledException)", 1)[1])
        self.assertIn('_roll = string.Empty;', build)
        self.assertNotIn("LoadCreationFinalizationInBackground", build)
        self.assertIn("private bool CurrentInput(long appearance, long version) => IsCurrentPage(appearance)"
                      " && version == _inputVersion && InputsCurrent();", source)
        self.assertIn("Coordinator.IsCreationCompletionInputsCurrent(_inputs)", source)
        review = source.split("private async Task ReviewAsync()", 1)[1].split("private void RenderFinalReview", 1)[0]
        self.assertIn("!InputsCurrent()", review)
        self.assertIn("!Coordinator.IsCreationCompletionProposalCurrent(proposal)", review)
        self.assertIn("_cashSources.FirstOrDefault(row => row.AuthorityDigest == _cashDigest)", review)
        self.assertIn("new(source.AuthorityDigest, dice)", review)

    def test_completion_resource_summary_displays_evaluated_additions_before_cash_review(self):
        source = (NATIVE / "CreationCompletionPage.cs").read_text(encoding="utf-8")
        proposal = source.split("private void RenderProposal()", 1)[1].split("private void RenderResourceProposal()", 1)[0]
        self.assertLess(proposal.index("RenderResourceProposal();"), proposal.index('Copy("Remaining"'))
        resources = source.split("private void RenderResourceProposal()", 1)[1].split("private void RenderLoadoutProposal()", 1)[0]
        self.assertIn("_proposal?.Evaluation.Value is not { } plan", resources)
        for identity, displayed in (
                ("native-language", "SkillCatalogStrings.SkillName(language.Kind, language.SourceSkillId, language.Name)"),
                ("contact", "contact.Identity.Name, contact.Connection, contact.Loyalty"),
                ("lifestyle", "home.BaseLifestyleName"),
                ("equipment", "item.Name + \" × \" + item.Quantity.ToString(CultureInfo.CurrentCulture)")):
            self.assertIn(f'value.AutomationId = "creation-completion-proposed-{identity}";', resources)
            self.assertIn(displayed, resources)
        self.assertIn("plan.Selections.SkillAllocations.SingleOrDefault(row => row.IsNativeLanguage)", resources)
        self.assertIn("plan.Selections.Contacts.Where", resources)
        self.assertIn("!inputs.Contacts.Contacts.Any(original => original.ContactId == row.ContactId)", resources)
        self.assertIn("plan.Review.Lifestyles is not { } purchases", resources)
        self.assertIn("!inputs.Lifestyles.Lifestyles.Any(original => original.LifestyleId == row.Configuration.LifestyleId)", resources)
        self.assertIn("Number(home.Economics.TotalCost)", resources)
        self.assertIn("purchases.Gear.Lines.Where", resources)
        self.assertIn("inputs.Lifestyles.Gear.State.PendingDraft?.Lines.Any(original => original.OptionId == row.OptionId) != true", resources)
        self.assertIn("Number(item.TotalCost)", resources)
        self.assertEqual(4, resources.count("_body.Add(value);"))
        self.assertNotIn("ApplyCreationCompletionAsync", resources)

    def test_completion_loadout_read_keeps_owner_revision_and_snapshot_guards(self):
        source = (NATIVE / "RunnerSessionCoordinator.CreationCompletion.cs").read_text(encoding="utf-8")
        read = source.split("LoadCreationCompletionInputsAsync(", 1)[1].split("BuildCreationCompletionProposalAsync(", 1)[0]
        dispatch = read.index("reader.LoadCompletionInputsWithLoadout(owner, new(workspaceId), cancellationToken)")
        before, after = read[:dispatch], read[dispatch:]
        for guard in ("WithWorkspaceActivationGateAsync", "cancellationToken.ThrowIfCancellationRequested()",
                      "!CompletionDisplayCurrent(original)", "original.DisplayOwnerContext is not { } owner",
                      "ReadCreationAuthority(original"):
            self.assertIn(guard, before)
        for guard in ("!CompletionDisplayCurrent(original)", "inputs.Loadout is not { } loadout",
                      "inputs.SnapshotDigest", "result.Outcome != CharacterCreationFoundationOutcomes.Success"):
            self.assertIn(guard, after)
        for binding in ("inputs.Skills.Binding", "loadout.Binding"):
            for member, expected in (("WorkspaceId", "workspaceId"), ("ContentRevision", "original.ContentRevision"),
                                     ("SavedRevision", "original.SavedRevision")):
                self.assertIn(f"{binding}.{member} != {expected}", after)
        self.assertNotIn("reader.LoadCompletionInputs(", read)

    def test_completion_loadout_summary_and_review_use_the_typed_core_selection(self):
        source = (NATIVE / "CreationCompletionPage.cs").read_text(encoding="utf-8")
        proposal = source.split("private void RenderProposal()", 1)[1].split("private void RenderMagicProposal()", 1)[0]
        self.assertLess(proposal.index("RenderLoadoutProposal();"), proposal.index('Copy("Remaining"'))
        loadout = source.split("private void RenderLoadoutProposal()", 1)[1].split("private void RenderCashAndReview", 1)[0]
        for displayed in ("_proposal?.Loadout", "foreach (var item in loadout.Lines)", "item.Name",
                          "item.Quantity.ToString(CultureInfo.CurrentCulture)", "Number(item.TotalCost)",
                          '"Completion.LoadoutLine"', '"creation-completion-proposed-loadout"'):
            self.assertIn(displayed, loadout)
        self.assertNotIn("OptionId", loadout)
        self.assertNotIn("SourceNodeXml", loadout)
        self.assertNotIn("ApplyCreationCompletionAsync", loadout)
        for task, key in (("ammo-modifications-rated-gear", "LoadoutExtras"),
                          ("ammo-binding-loading-modifications", "AmmoExtras"),
                          ("commlinks-rated-gear-child-graphs", "AdvancedGear"), ("weapon-choices", "WeaponNeeded"),
                          ("armor-choices", "ArmorNeeded")):
            self.assertIn(f'"completion.remaining.{task}" => Copy("{key}"', source)
        coordinator = (NATIVE / "RunnerSessionCoordinator.CreationCompletion.cs").read_text(encoding="utf-8")
        review = coordinator.split("ReviewCreationCompletionAsync(", 1)[1].split("ApplyCreationCompletionAsync(", 1)[0]
        self.assertIn("new CharacterCreationCompletionReviewRequest(frozen.Intent.InputsSnapshotDigest, plan.Selections)", review)
        self.assertIn("service.Review(owner, workspaceId, request, startingCash, cancellationToken)", review)

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
        keys.update({"Completion.Rating", "Completion.ContactAddition", "Completion.LoadoutLine"})
        # BuildPage contains other copy helpers; only this page and its named entry use this prefix.
        keys = {key for key in keys if key != "Completion.Receipt"}
        resources = NATIVE.parent / "Resources" / "Localization"
        for suffix in ("", ".de", ".es"):
            root = ET.parse(resources / f"CreationFlowStrings{suffix}.resx").getroot()
            values = {entry.attrib["name"]: entry.findtext("value") for entry in root.findall("data")}
            for key in keys:
                self.assertTrue(values.get(key), (suffix, key))
            allocation = ET.parse(resources / f"CreationAllocationStrings{suffix}.resx").getroot()
            for key in re.findall(r'CreationAllocationStrings.Get\("(Completion\.[^"]+)"', source):
                entries = [entry for entry in allocation.findall("data") if entry.attrib["name"] == key]
                self.assertEqual(1, len(entries), (suffix, key))
                self.assertTrue(entries[0].findtext("value"), (suffix, key))
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
