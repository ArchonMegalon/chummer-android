import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
NATIVE = REPO / "src" / "Chummer.Android" / "Native"


class CreationAttributesSourceContractTests(unittest.TestCase):
    def test_phone_stage_is_core_projected_typed_and_draft_only(self) -> None:
        page = (NATIVE / "CreationAttributesPage.cs").read_text(encoding="utf-8")
        draft = (NATIVE / "CreationAttributesPhoneDraft.cs").read_text(encoding="utf-8")
        authority = (NATIVE / "CreationAttributesPhoneAuthority.cs").read_text(
            encoding="utf-8"
        )
        coordinator = (NATIVE / "RunnerSessionCoordinator.cs").read_text(
            encoding="utf-8"
        )

        for marker in (
            'AutomationId = "creation-attributes-page"',
            "Coordinator.LoadCreationAttributes()",
            "Coordinator.PreviewCreationAttributes(state.Binding, allocations)",
            "CharacterCreationAttributeAllocation",
            "CharacterCreationAttributeProjection",
            "_draft.NormalBudget(state)",
            "_draft.SpecialBudget(state)",
            "_draft.KarmaBudget(state)",
            'AutomationId = "creation-attributes-prepare-preview"',
            'AutomationId = "creation-attributes-preview-page"',
            'AutomationId = "creation-attributes-confirm"',
            "Coordinator.ConfirmCreationAttributesAsync(",
            'AutomationId = "creation-attributes-confirm-receipt"',
            "CharacterEffectsApplied",
            "pending finalization",
        ):
            self.assertIn(marker, page)

        for marker in (
            "CreationAttributesPhoneAuthority.IsReady(state, overview)",
            "CreationAttributesPhoneAuthority.BindingEquals(_binding, state.Binding)",
            "state.PendingDraft?.Allocations",
            "ChangedAllocations(",
            "Coordinator.State",
        ):
            self.assertIn(marker, draft + page)

        for marker in (
            "CharacterCreationAttributesSchemas.SnapshotV1",
            "CharacterCreationAttributesSchemas.PreviewV1",
            "CharacterCreationAttributesSchemas.DraftV1",
            "CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime",
            "BindingEquals(",
            "CanAdoptPreview(",
            "CanConfirmPreview(",
            "CharacterCreationFoundationOutcomes.Success",
            "preview.RequiresExplicitConfirmation",
            "preview.CanConfirm",
            "IsCanonicalDigest(preview.PreviewDigest)",
            "CanonicallyEquals(",
            "ReceiptMatchesBeforeActivation(",
            "ReceiptMatches(",
            "AllocationIdentitiesMatch(",
            "ProjectionIdentitiesMatch(",
            "!receipt.CharacterDocumentChanged",
            "!pending.CharacterEffectsApplied",
        ):
            self.assertIn(marker, authority)

        for marker in (
            "ICharacterCreationAttributesService? _creationAttributesService",
            "LoadCreationAttributes()",
            "PreviewCreationAttributes(",
            "ConfirmCreationAttributesAsync(",
            "new CharacterCreationAttributesConfirmRequest(",
            "canonicalPreview.PreviewDigest",
            "ExplicitlyConfirmed: true",
            "_presenter.LoadAsync(receipt.WorkspaceId",
            "CreationAttributesPhoneAuthority.ReceiptMatches(",
        ):
            self.assertIn(marker, coordinator)

        for forbidden in (
            "System.Xml",
            "XmlDocument",
            "XDocument",
            "XElement",
            "ApplyAttributeEditAsync",
            "AttributeEditRequest",
        ):
            self.assertNotIn(forbidden, page + draft + authority)

    def test_each_adjustment_requires_a_fresh_core_preview(self) -> None:
        page = (NATIVE / "CreationAttributesPage.cs").read_text(encoding="utf-8")
        allocation = page[page.index("public sealed class CreationAttributeAllocationPage") :]
        allocation = allocation[: allocation.index("public sealed class CreationAttributesPreviewPage")]

        self.assertIn("_draft.ChangedAllocations(", allocation)
        self.assertIn("Coordinator.PreviewCreationAttributes(state.Binding, allocations)", allocation)
        self.assertIn("CreationAttributesPhoneAuthority.CanAdoptPreview(", allocation)
        self.assertIn("_draft.TryAdopt(state, Coordinator.State, result!, allocations!)", allocation)
        self.assertNotIn("KarmaAttribute", allocation)
        self.assertNotIn("PriorityPointCost +", allocation)

    def test_dashboard_overrides_stale_generic_stage_only_with_exact_authority(self) -> None:
        dashboard = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")

        for marker in (
            "Coordinator.LoadCreationAttributes",
            "HasAuthoritativeAttributes(attributes)",
            "CreationAttributesPhoneAuthority.IsReady(state, Coordinator.State)",
            "OpenCreationAttributesAsync",
            "AttributeStageDetail(",
            "attributeState.NormalPointBudget",
            "attributeState.SpecialPointBudget",
            "attributeState.CreationKarmaBudget",
        ):
            self.assertIn(marker, dashboard)
        self.assertIn("The post-create AttributeEditRequest path must never serve", dashboard)

    def test_confirmation_reprojects_and_validates_committed_state_before_activation(self) -> None:
        coordinator = (NATIVE / "RunnerSessionCoordinator.cs").read_text(
            encoding="utf-8"
        )
        confirmation = coordinator[
            coordinator.index("ConfirmCreationAttributesCoreAsync(") :
        ]
        confirmation = confirmation[: confirmation.index("LoadCreationFoundation()")]

        preview_index = confirmation.index("_creationAttributesService.Preview(")
        equality_index = confirmation.index(
            "CreationAttributesPhoneAuthority.CanonicallyEquals("
        )
        confirm_index = confirmation.index("_creationAttributesService.Confirm(")
        direct_load_index = confirmation.index("_creationAttributesService.Load(")
        receipt_index = confirmation.index(
            "CreationAttributesPhoneAuthority.ReceiptMatchesBeforeActivation("
        )
        presenter_load_index = confirmation.index(
            "_presenter.LoadAsync(receipt.WorkspaceId"
        )
        shell_index = confirmation.index("SyncShellAsync(cancellationToken)")

        self.assertLess(preview_index, equality_index)
        self.assertLess(equality_index, confirm_index)
        self.assertLess(confirm_index, direct_load_index)
        self.assertLess(direct_load_index, receipt_index)
        self.assertLess(receipt_index, presenter_load_index)
        self.assertLess(presenter_load_index, shell_index)


if __name__ == "__main__":
    unittest.main()
