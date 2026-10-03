from __future__ import annotations

import os
import re
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
WORKSPACE = Path(os.environ.get("CHUMMER_COMPLETE_ROOT", ROOT.parent))
PRESENTATION_ROOT = WORKSPACE / "chummer-presentation"
PRESENTER = PRESENTATION_ROOT / "Chummer.Presentation/Overview/CharacterCreationResourcesInteractionPresenter.cs"
PAGE = ROOT / "src/Chummer.Android/Native/CreationResourcesPage.cs"
BUILD_PAGE = ROOT / "src/Chummer.Android/Native/BuildPage.cs"
MAUI_PROGRAM = ROOT / "src/Chummer.Android/MauiProgram.cs"


def source(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class CreationResourcesSourceContractTests(unittest.TestCase):
    def test_presentation_boundary_is_typed_and_renderer_neutral(self) -> None:
        text = source(PRESENTER)
        self.assertIn("interface ICharacterCreationResourcesInteractionPresenter", text)
        for signature in (
            "CharacterCreationResourcesInteractionLoadResult Load(",
            "CharacterCreationResourcesInteractionPrepareResult Prepare(",
            "CharacterCreationResourcesInteractionConfirmResult Confirm(",
            "CharacterCreationResourcesInteractionReceiptLookupResult LookupReceipt(",
        ):
            self.assertIn(signature, text)
        self.assertIn("ICharacterCreationResourcesService _service", text)

    def test_prepare_and_confirm_use_only_exact_core_options(self) -> None:
        text = source(PRESENTER)
        self.assertIn("resources.Options.SingleOrDefault", text)
        self.assertIn("option is null || !option.IsEnabled || option.Blockers.Count != 0", text)
        self.assertIn("new CharacterCreationResourcesPreviewRequest(", text)
        self.assertIn("new CharacterCreationResourcesConfirmRequest(", text)
        self.assertNotIn("KarmaToNuyenRate *", text)
        self.assertNotIn("new CharacterCreationResourceAllocationOption(", text)

    def test_confirmation_reloads_and_repreviews_before_commit(self) -> None:
        text = source(PRESENTER)
        confirm = text[text.index("public CharacterCreationResourcesInteractionConfirmResult Confirm(") :]
        self.assertLess(confirm.index("ExactLoad load = LoadExact(overview)"), confirm.index("ServiceFor(overview).Confirm(request)"))
        self.assertLess(confirm.index("CharacterCreationResourcesResult<CharacterCreationResourcesPreview> repreview"), confirm.index("ServiceFor(overview).Confirm(request)"))
        self.assertIn("PreparedMatchesPreview(prepared, currentPreview)", confirm)
        self.assertIn("ServiceFor(overview).Load(new CharacterCreationResourcesLoadRequest(receipt.WorkspaceId))", confirm)

    def test_confirmation_and_receipts_are_digest_bound_and_fail_closed(self) -> None:
        text = source(PRESENTER)
        for expression in (
            "CharacterCreationResourcesBlockers.ExplicitConfirmationRequired",
            "CharacterCreationResourcesBlockers.PreviewDigestMismatch",
            "CharacterCreationResourcesRules.ComputePreviewDigest(preview)",
            "CharacterCreationResourcesRules.ComputeCommandDigest(request)",
            "CharacterCreationResourcesRules.ComputeReceiptDigest(receipt)",
            "CommittedDraftDigestMatches(prepared, receipt)",
            '"chummer.sr5.creation-resources.idempotency.v1\\0" + value',
        ):
            self.assertIn(expression, text)
        self.assertNotIn("CharacterDocumentChanged: true", text)

    def test_phone_catalog_exposes_exact_core_budget_and_options(self) -> None:
        text = source(PAGE)
        for automation_id in (
            "creation-resources-page",
            "creation-resources-binding",
            "creation-resources-budget",
            "creation-resources-saved-draft",
            "creation-resources-authority",
        ):
            self.assertIn(automation_id, text)
        self.assertIn("foreach (CharacterCreationResourceAllocationOption option in state.Options", text)
        self.assertIn("option.IsEnabled && option.Blockers.Count == 0", text)
        self.assertIn('"creation-resources-option-{Token(option.OptionId)}"', text)
        self.assertNotIn("new CharacterCreationResourcesBudget(", text)

    def test_phone_flow_separates_preview_from_explicit_confirmation(self) -> None:
        text = source(PAGE)
        for automation_id in (
            "creation-resources-preview-page",
            "creation-resources-preview-budget",
            "creation-resources-preview-contribution",
            "creation-resources-confirm",
            "creation-resources-confirm-receipt",
            "creation-resources-reopen",
        ):
            self.assertIn(automation_id, text)
        self.assertIn("_resources.Prepare(", text)
        coordinator = source(ROOT / "src/Chummer.Android/Native/RunnerSessionCoordinator.CreationPurchases.cs")
        self.assertIn("ExplicitlyConfirmed: true", coordinator)
        self.assertIn("ConfirmCreationResourcesPurchaseAsync(_resources, _original, _prepared)", text)
        self.assertIn("await bound.LoadBeforeShellSyncAsync(owner, workspaceId, ct)", coordinator)
        self.assertNotIn("await bound.LoadAsync(owner, workspaceId, ct)", coordinator)
        self.assertNotIn("await _overview.LoadAsync(", text)
        self.assertIn("presenter.Load(refreshedDisplay)", coordinator)

    def test_blocked_resources_keeps_full_diagnostics_after_budget_and_blockers(self) -> None:
        text = source(PAGE)
        refresh = text.split("protected override void Refresh()", 1)[1].split(
            "private void AddGearRoute", 1
        )[0]
        blocked = refresh.split(
            "if (!_ready)", 1
        )[1].split("return;", 1)[0]
        self.assertLess(refresh.index("AddBudget("), refresh.index("AddBinding(state)"))
        self.assertLess(refresh.index("AddBinding(state)"), refresh.index("if (!_ready)"))
        self.assertLess(blocked.index("AddBlockers("), blocked.index("AddTechnicalDetailsDisclosure()"))
        self.assertIn("_technicalDetails.IsVisible = false", refresh)
        self.assertIn("_body.Add(_technicalDetails)", text)
        self.assertIn("_technicalDetails.IsVisible = !_technicalDetails.IsVisible", text)

    def test_phone_flow_exposes_full_values_needed_for_physical_receipts(self) -> None:
        text = source(PAGE)
        for automation_id in (
            "creation-resources-binding-content-revision",
            "creation-resources-binding-saved-revision",
            "creation-resources-binding-snapshot-digest",
            "creation-resources-binding-raw-character-xml-digest",
            "creation-resources-binding-auxiliary-state-digest",
            "creation-resources-binding-prerequisite-draft-digest",
            "creation-resources-preview-option-id",
            "creation-resources-preview-priority-grant",
            "creation-resources-preview-total-starting-nuyen",
            "creation-resources-preview-digest",
            "creation-resources-receipt-option-id",
            "creation-resources-receipt-workspace-revision",
            "creation-resources-receipt-saved-revision",
            "creation-resources-receipt-draft-revision",
            "creation-resources-receipt-total-starting-nuyen",
            "creation-resources-receipt-preview-digest",
            "creation-resources-receipt-draft-digest",
            "creation-resources-receipt-digest",
            "creation-resources-saved-option-id",
            "creation-resources-saved-draft-revision",
            "creation-resources-saved-draft-digest",
        ):
            self.assertIn(automation_id, text)
        self.assertIn('AddBudget(state.Budget, _copy["Resources.CurrentBudget"], "creation-resources-budget")', text)
        self.assertIn('$"{automationId}-priority-nuyen"', text)
        self.assertIn('$"{automationId}-total-starting-nuyen"', text)

    def test_phone_authority_rejects_revision_option_preview_and_receipt_drift(self) -> None:
        text = source(PAGE)
        for expression in (
            "state.Binding.WorkspaceRevision == overview.ContentRevision",
            "state.Binding.SavedRevision == overview.SavedRevision",
            "state.Options.Count(option => string.Equals(",
            "CharacterCreationResourcesRules.ComputePreviewDigest(new CharacterCreationResourcesPreview(",
            "receipt.WorkspaceRevision == receipt.PreviousWorkspaceRevision + 1",
            "CharacterCreationResourcesRules.ComputeReceiptDigest(receipt)",
            "draft.DraftRevision == receipt.DraftRevision",
        ):
            self.assertIn(expression, text)

    def test_phone_authority_uses_the_workspace_raw_auxiliary_sha256_contract(self) -> None:
        text = source(PAGE)
        self.assertIn("IsLowerRawSha256(state.Binding.AuxiliaryStateDigest)", text)
        self.assertNotIn(
            "CharacterCreationResourcesRules.IsCanonicalDigest(state.Binding.AuxiliaryStateDigest)",
            text,
        )
        for digest in (
            "state.SnapshotDigest",
            "state.Binding.PrerequisiteDraftDigest",
            "state.Binding.AuthorityDigest",
            "state.Binding.SourceDigest",
            "state.Binding.RulesDigest",
            "state.Binding.RuntimeDigest",
        ):
            self.assertIn(
                f"CharacterCreationResourcesRules.IsCanonicalDigest({digest})",
                text,
            )

    def test_build_dashboard_loads_resources_as_a_bound_background_phase(self) -> None:
        text = source(BUILD_PAGE)
        for expression in (
            "CreationDashboardAuthorityPhase.Resources",
            "CharacterCreationResourcesInteractionLoadResult? Resources",
            "_creationResourcesQueue",
            "_resourcesPresenter.Load(resourcesOverview)",
            "HasAuthoritativeResources(creationResources)",
            "OpenCreationResourcesAsync",
            "CreationResourcesStageDetail",
        ):
            self.assertIn(expression, text)
        self.assertIn("_creationResourcesQueue.Cancel()", text)

    def test_di_registers_core_service_backed_presenter(self) -> None:
        text = source(MAUI_PROGRAM)
        registration = re.compile(
            r"AddSingleton<ICharacterCreationResourcesInteractionPresenter>\(provider\s*=>\s*"
            r"new CharacterCreationResourcesInteractionPresenter\(\s*"
            r"provider\.GetRequiredService<ICharacterCreationResourcesService>\(\),\s*"
            r"provider\.GetRequiredService<IOwnerBoundCharacterCreationResourcesService>\(\)\)\)",
            re.MULTILINE,
        )
        self.assertRegex(text, registration)

    def test_resources_slice_has_no_direct_character_xml_write_surface(self) -> None:
        combined = source(PRESENTER) + "\n" + source(PAGE)
        forbidden = (
            "XDocument",
            "XElement",
            "WorkspaceDocument(",
            "ReplaceWorkspace",
            "character:write",
            "ApplyCharacter",
        )
        for token in forbidden:
            self.assertNotIn(token, combined)

    def test_starting_cash_input_keeps_theme_contrast_and_explicit_numeric_choice(self) -> None:
        text = source(ROOT / "src/Chummer.Android/Native/CreationFinalizationPage.cs")
        entry = text.split("var input = new Entry", 1)[1].split("};", 1)[0]
        self.assertIn("TextColor = NativeTheme.Text", entry)
        self.assertIn("BackgroundColor = NativeTheme.Surface", entry)
        self.assertIn("Keyboard = Keyboard.Numeric", entry)
        self.assertIn("Text = _roll", entry)
        self.assertIn('AutomationId = "creation-starting-cash-roll"', entry)

    def test_original_owner_background_read_and_known_commit_are_retained(self) -> None:
        page = source(PAGE)
        presenter = source(PRESENTER)
        coordinator = source(ROOT / "src/Chummer.Android/Native/RunnerSessionCoordinator.CreationPurchases.cs")
        preview = page.split("public sealed class CreationResourcesPreviewPage", 1)[1]
        self.assertIn("Task.Run(() => Coordinator.ReadCreationAuthority(_original", preview)
        render = preview.split("protected override void Refresh()", 1)[1].split("private async Task ConfirmAsync()", 1)[0]
        self.assertNotIn("_resources.Load(", render)
        self.assertIn("!_submitted && _ready", render)
        self.assertIn("Coordinator.CanDisplayCreationPurchase(_original)", render)
        self.assertLess(preview.index("_receipt = receipt"), preview.index("result.RefreshedState is not"))
        self.assertIn("_preparedOwners.TryGetValue(prepared", presenter)
        self.assertIn("overview.DisplayOwnerContext != admitted.Stamp", presenter)
        self.assertIn("result.Outcome, prepared, receipt, null", presenter)
        self.assertIn("ConditionalWeakTable<object, CreationPurchaseAttempt>", coordinator)
        self.assertIn("Interlocked.CompareExchange(ref attempt.Started, 1, 0)", coordinator)
        self.assertIn("return NeedsReopen()", coordinator)


if __name__ == "__main__":
    unittest.main()
