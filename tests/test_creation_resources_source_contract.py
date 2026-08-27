from __future__ import annotations

import re
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PRESENTATION_ROOT = ROOT.parent / "chummer-presentation"
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
        self.assertLess(confirm.index("ExactLoad load = LoadExact(overview)"), confirm.index("_service.Confirm(request)"))
        self.assertLess(confirm.index("CharacterCreationResourcesResult<CharacterCreationResourcesPreview> repreview"), confirm.index("_service.Confirm(request)"))
        self.assertIn("PreparedMatchesPreview(prepared, currentPreview)", confirm)
        self.assertIn("_service.Load(new CharacterCreationResourcesLoadRequest(receipt.WorkspaceId))", confirm)

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
        self.assertIn("ExplicitlyConfirmed: true", text)
        self.assertIn("await _overview.LoadAsync(receipt.WorkspaceId", text)
        self.assertIn("CharacterCreationResourcesInteractionLoadResult reopened = _resources.Load(_overview.State)", text)

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
        self.assertIn('AddBudget(state.Budget, "Current exact budget", "creation-resources-budget")', text)
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
            r"provider\.GetRequiredService<ICharacterCreationResourcesService>\(\)\)\)",
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


if __name__ == "__main__":
    unittest.main()
