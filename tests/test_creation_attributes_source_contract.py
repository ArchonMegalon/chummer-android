import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
NATIVE = REPO / "src" / "Chummer.Android" / "Native"


class CreationAttributesSourceContractTests(unittest.TestCase):
    def test_completed_allocation_returns_through_attached_phone_shell(self) -> None:
        for name in ("CreationAttributesPage.cs", "CreationSkillsPage.cs"):
            with self.subTest(page=name):
                page = (NATIVE / name).read_text(encoding="utf-8")
                route = page[page.index("private async Task BackToBuildAsync()") :]
                route = route[: route.index("private void AddDigest(")]
                self.assertTrue("await RunAsync(BackToBuildAsync)" in page,
                                "Back-to-Build must use the page's single-action guard")
                self.assertIn("MainShell { UsesTabletComposition: false } shell", route)
                self.assertIn("await shell.GoToAsync(PhoneShellRoutes.RunnerAbsolute, animate: false)", route)
                self.assertNotIn("Navigation.Pop", route)

    def test_phone_stage_is_core_projected_typed_and_draft_only(self) -> None:
        page = (NATIVE / "CreationAttributesPage.cs").read_text(encoding="utf-8")
        draft = (NATIVE / "CreationAttributesPhoneDraft.cs").read_text(encoding="utf-8")
        authority = (NATIVE / "CreationAttributesPhoneAuthority.cs").read_text(
            encoding="utf-8"
        )
        coordinator = (NATIVE / "RunnerSessionCoordinator.cs").read_text(
            encoding="utf-8"
        ) + (NATIVE / "RunnerSessionCoordinator.CreationAttributes.cs").read_text(encoding="utf-8")

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
            "Finish character creation separately to enter Career.",
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
            "IOwnerBoundCharacterCreationAttributesService? _ownerBoundAttributesService",
            "LoadCreationAttributes()",
            "PreviewCreationAttributes(",
            "ConfirmCreationAttributesAsync(",
            "service.Confirm(owner, new(canonical.Binding, allocations, canonical.PreviewDigest, true))",
            "issued.Load.Display.DisplayOwnerContext is not { } owner",
            "IOwnerBoundWorkspaceRefreshPresenter bound",
            "bound.LoadAsync(owner, receipt.WorkspaceId",
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

        self.assertIn("var draft = _draft.Copy()", allocation)
        self.assertIn("draft.ChangedAllocations(", allocation)
        self.assertIn("Coordinator.PreviewCreationAttributes(state.Binding, allocations)", allocation)
        self.assertIn("CreationAttributesPhoneAuthority.CanAdoptPreview(", allocation)
        self.assertIn("_draft.TryAdopt(state, Coordinator.State, result!, allocations!)", allocation)
        self.assertNotIn("KarmaAttribute", allocation)
        self.assertNotIn("PriorityPointCost +", allocation)

    def test_attribute_render_does_not_synchronously_read_core(self) -> None:
        page = (NATIVE / "CreationAttributesPage.cs").read_text(encoding="utf-8")
        allocation = page[page.index("public sealed class CreationAttributeAllocationPage") :]
        allocation = allocation[: allocation.index("public sealed class CreationAttributesPreviewPage")]
        render = allocation[allocation.index("protected override void Refresh()") :]
        self.assertNotIn("Coordinator.RevalidateCreationAttributes(", render)
        self.assertNotIn("Coordinator.PreviewCreationAttributes(", render)
        self.assertIn("Task.Run(() => Coordinator.ReadCreationAuthority(original", allocation)
        self.assertIn("generation != _preparationGeneration", allocation)
        self.assertIn("_preparation?.Cancel()", allocation)
        self.assertIn("SequenceEqual(prepared.Allocations)", allocation)

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
        coordinator = (NATIVE / "RunnerSessionCoordinator.CreationAttributes.cs").read_text(
            encoding="utf-8"
        )
        confirmation = coordinator[
            coordinator.index("private async Task<CreationAttributesPhoneConfirmResult> ConfirmIssuedAttributesAsync(") :
        ]

        preview_index = confirmation.index("service.Preview(owner,")
        equality_index = confirmation.index(
            "CreationAttributesPhoneAuthority.CanonicallyEquals("
        )
        confirm_index = confirmation.index("service.Confirm(owner,")
        direct_load_index = confirmation.index("service.Load(owner, new(receipt.WorkspaceId))")
        receipt_index = confirmation.index(
            "CreationAttributesPhoneAuthority.ReceiptMatchesBeforeActivation("
        )
        presenter_load_index = confirmation.index(
            "bound.LoadAsync(owner, receipt.WorkspaceId"
        )
        shell_index = confirmation.index("SyncShellAsync(ct)")

        self.assertLess(preview_index, equality_index)
        self.assertLess(equality_index, confirm_index)
        self.assertLess(confirm_index, direct_load_index)
        self.assertLess(direct_load_index, receipt_index)
        self.assertLess(receipt_index, presenter_load_index)
        self.assertLess(presenter_load_index, shell_index)
        self.assertIn("issued.ConfirmationStarted = true", confirmation)
        self.assertIn("IsCreationAttributesPreviewCurrent(preview)", confirmation)
        self.assertIn("IsNativePersistenceOwnerCurrent(owner)", confirmation)
        self.assertIn("creation-attributes-post-commit-refresh-required", confirmation)
        self.assertNotIn("_creationAttributesService", coordinator)


if __name__ == "__main__":
    unittest.main()
