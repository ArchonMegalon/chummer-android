import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
NATIVE = REPO / "src" / "Chummer.Android" / "Native"


class CreationSkillsSourceContractTests(unittest.TestCase):
    def test_phone_stage_consumes_only_core_skills_projections(self) -> None:
        page = (NATIVE / "CreationSkillsPage.cs").read_text(encoding="utf-8")
        draft = (NATIVE / "CreationSkillsPhoneDraft.cs").read_text(encoding="utf-8")
        authority = (NATIVE / "CreationSkillsPhoneAuthority.cs").read_text(
            encoding="utf-8"
        )
        coordinator = (NATIVE / "RunnerSessionCoordinator.cs").read_text(
            encoding="utf-8"
        )

        for marker in (
            'AutomationId = "creation-skills-page"',
            "Coordinator.LoadCreationSkills()",
            "state.Authority.ActiveSkills",
            "state.Authority.KnowledgeSkills",
            "state.Authority.SkillGroups",
            "CreationSkillsCatalogPaging.NormalizeOffset(",
            ".Take(CatalogPageSize)",
            'AutomationId = $"creation-skills-{catalogToken}-catalog-range"',
            "projection?.ActiveSkillPointBudget ?? state.ActiveSkillPointBudget",
            "projection?.SkillGroupPointBudget ?? state.SkillGroupPointBudget",
            "projection?.KnowledgeSkillPointBudget ?? state.KnowledgeSkillPointBudget",
            "await Task.Run(() => Coordinator.PreviewCreationSkills(",
            "new CreationSkillsPreviewPage(",
            'AutomationId = "creation-skills-preview-page"',
            'AutomationId = "creation-skills-confirm"',
            "Coordinator.ConfirmCreationSkillsAsync(",
            'AutomationId = "creation-skills-confirm-receipt"',
            "pending finalization",
        ):
            self.assertIn(marker, page)

        for marker in (
            "CreationSkillsPhoneAuthority.IsReady(state, overview)",
            "CreationSkillsPhoneAuthority.BindingEquals(_binding, state.Binding)",
            "state.PendingDraft?.Allocations",
            "state.PendingDraft?.GroupAllocations",
            "CharacterCreationSkillsDigest.EqualsFixedTime(_snapshotDigest, state.SnapshotDigest)",
            "current.SpecializationOptionId",
            "source.CanBeNativeLanguage",
            "foreach (CharacterCreationSkillProjection item in preview.Skills)",
            "foreach (CharacterCreationSkillGroupProjection item in preview.SkillGroups)",
        ):
            self.assertIn(marker, draft)

        for marker in (
            "CharacterCreationSkillsSchemas.SnapshotV1",
            "CharacterCreationSkillsSchemas.PreviewV1",
            "BindingEquals(",
            "CanAdoptPreview(",
            "CanConfirmPreview(",
            "preview.RequiresExplicitConfirmation",
            "preview.CanConfirm",
            "CanonicallyEquals(",
            "ComputeIdempotencyKey(",
            'Schema = "chummer.android.creation-skills-idempotency.v1"',
            "ReceiptMatchesBeforeActivation(",
            "ReceiptMatches(",
            "CharacterCreationSkillsDigest.IsValidReceipt(",
            "!receipt.CharacterDocumentChanged",
            "CharacterCreationSkillsBlockers.PostCommitRefreshRequired",
            "CharacterCreationSkillsDraftIntegrity.IsValidStateProjection(state)",
        ):
            self.assertIn(marker, authority)

        for marker in (
            "ICharacterCreationSkillsService? _creationSkillsService",
            "LoadCreationSkills()",
            "PreviewCreationSkills(",
            "ConfirmCreationSkillsAsync(",
            "new(canonical.Binding, allocations.ToArray(), groups.ToArray(),",
            "canonical.PreviewDigest, idempotencyKey, true",
            "_creationSkillsService.Load(new(receipt.WorkspaceId))",
            "_presenter.LoadAsync(receipt.WorkspaceId",
            "CreationSkillsPhoneAuthority.ReceiptMatches(",
            "CommittedSkillsRefreshRequired(",
            "CreationSkillsPhoneAuthority.CommittedRefreshRequired(",
        ):
            self.assertIn(marker, coordinator)

        combined = page + draft + authority
        for forbidden in (
            "System.Xml",
            "XmlDocument",
            "XDocument",
            "XElement",
            "ApplySkillEdit",
            "SkillEditRequest",
            "AttributeEditRequest",
            "Guid.NewGuid",
            "({INTUnaug} + {LOGUnaug}) * 2",
            'source.Category, "Language"',
        ):
            self.assertNotIn(forbidden, combined)

    def test_every_adjustment_requires_a_fresh_core_preview(self) -> None:
        page = (NATIVE / "CreationSkillsPage.cs").read_text(encoding="utf-8")
        for mutation in ("_draft.WithSkill(", "_draft.WithGroup(", "_draft.WithSpecialization("):
            self.assertIn(mutation, page)
        self.assertIn("await Task.Run(() => Coordinator.PreviewCreationSkills(", page)
        self.assertIn("requestedSkills", page)
        self.assertIn("requestedGroups", page)
        self.assertIn("_draft.TryAdopt(", page)
        self.assertIn("requestedSkills,", page)
        self.assertIn("requestedGroups);", page)
        self.assertNotIn("ActivePointTotal =", page)
        self.assertNotIn("KnowledgePointTotal =", page)

    def test_confirmation_reprojects_then_validates_receipt_before_activation(self) -> None:
        coordinator = (NATIVE / "RunnerSessionCoordinator.cs").read_text(
            encoding="utf-8"
        )
        confirmation = coordinator[
            coordinator.index("ConfirmCreationSkillsCoreAsync(") :
        ]
        confirmation = confirmation[: confirmation.index("LoadCreationFoundation()")]

        preview_index = confirmation.index("_creationSkillsService.Preview(")
        equality_index = confirmation.index("CreationSkillsPhoneAuthority.CanonicallyEquals(")
        confirm_index = confirmation.index("_creationSkillsService.Confirm(")
        direct_load_index = confirmation.index(
            "_creationSkillsService.Load(new(receipt.WorkspaceId))"
        )
        receipt_index = confirmation.index(
            "CreationSkillsPhoneAuthority.ReceiptMatchesBeforeActivation("
        )
        presenter_load_index = confirmation.index("_presenter.LoadAsync(receipt.WorkspaceId")
        shell_index = confirmation.index("SyncShellAsync(cancellationToken)")

        self.assertLess(preview_index, equality_index)
        self.assertLess(equality_index, confirm_index)
        self.assertLess(confirm_index, direct_load_index)
        self.assertLess(direct_load_index, receipt_index)
        self.assertLess(receipt_index, presenter_load_index)
        self.assertLess(presenter_load_index, shell_index)
        self.assertIn(
            "return CommittedSkillsRefreshRequired(receipt, committedState, refreshed.Blockers)",
            confirmation,
        )

    def test_dashboard_uses_core_ledgers_and_never_post_create_editor(self) -> None:
        dashboard = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")
        for marker in (
            "Coordinator.LoadCreationSkills",
            "HasAuthoritativeSkills(skills)",
            "CreationSkillsPhoneAuthority.IsReady(state, Coordinator.State)",
            "OpenCreationSkillsAsync",
            "SkillsStageDetail(",
            "skillState.ActiveSkillPointBudget",
            "skillState.SkillGroupPointBudget",
            "skillState.KnowledgeSkillPointBudget",
        ):
            self.assertIn(marker, dashboard)
        self.assertNotIn("SkillEditRequest", dashboard)


if __name__ == "__main__":
    unittest.main()
