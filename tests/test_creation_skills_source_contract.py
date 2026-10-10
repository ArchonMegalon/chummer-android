import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
NATIVE = REPO / "src" / "Chummer.Android" / "Native"


class CreationSkillsSourceContractTests(unittest.TestCase):
    def test_specializations_use_complete_catalog_and_explicit_preview(self) -> None:
        page = (NATIVE / "CreationSkillsPage.cs").read_text(encoding="utf-8")
        self.assertNotIn("source.Specializations.Take(", page)
        self.assertIn("source.Specializations.ToArray()", page)
        self.assertIn("options.Select(option => SkillCatalogStrings.SpecializationName(", page)
        self.assertIn("options[index - 1].OptionId", page)
        self.assertIn("index == currentIndex", page)
        self.assertIn("renderGeneration != _renderGeneration", page)
        self.assertIn("!IsCurrentAppearanceGeneration(appearanceGeneration)", page)
        selection = page[page.index("picker.SelectedIndexChanged +="):page.index("long renderGeneration =")]
        self.assertNotIn("PreviewAsync", selection)
        self.assertNotIn("WithSpecialization", selection)

    def test_dashboard_reads_wait_for_original_owner_only_on_background_worker(self) -> None:
        page = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")
        start = page.index("private void ResolveCreationPhase<TResult>")
        phase = page[start:page.index("private void ScheduleCreationPhaseAcceptance", start)]
        self.assertLess(phase.index("CharacterOverviewState original = Coordinator.State"), phase.index("queue.TryRequest("))
        self.assertIn("Coordinator.ReadCreationAuthority(original, loader, cancellationToken)", phase)
        coordinator = (NATIVE / "RunnerSessionCoordinator.CreationAttributes.cs").read_text(encoding="utf-8")
        self.assertIn("androidOwner.RunScheduledRead(owner, read, ct)", coordinator)
        self.assertIn("if (!AttributesDisplayCurrent(original))", coordinator)
        accessor = (NATIVE / "AndroidAccountOwnerContextAccessor.cs").read_text(encoding="utf-8")
        self.assertIn("finally { _readAdmission = previous; }", accessor)
        self.assertIn("read!.Expected != expected", accessor)
        self.assertIn("IsActiveOnCurrentThread: true", accessor)

    def test_phone_stage_consumes_only_core_skills_projections(self) -> None:
        page = (NATIVE / "CreationSkillsPage.cs").read_text(encoding="utf-8")
        draft = (NATIVE / "CreationSkillsPhoneDraft.cs").read_text(encoding="utf-8")
        authority = (NATIVE / "CreationSkillsPhoneAuthority.cs").read_text(
            encoding="utf-8"
        )
        coordinator = (NATIVE / "RunnerSessionCoordinator.cs").read_text(
            encoding="utf-8"
        ) + (NATIVE / "RunnerSessionCoordinator.CreationSkills.cs").read_text(encoding="utf-8")

        for marker in (
            'AutomationId = "creation-skills-page"',
            "Coordinator.LoadCreationSkills()",
            "CreationSkillsPhoneAuthority.AvailableActiveSkills(state)",
            "state.Authority.KnowledgeSkills",
            "CreationSkillsPhoneAuthority.AvailableGroups(state)",
            "CreationSkillsCatalogPaging.NormalizeOffset(",
            ".Take(CatalogPageSize)",
            'AutomationId = $"creation-skills-{catalogToken}-catalog-range"',
            "_draft.Preview?.ActiveSkillPointBudget ?? state.ActiveSkillPointBudget",
            "_draft.Preview?.SkillGroupPointBudget ?? state.SkillGroupPointBudget",
            "_draft.Preview?.KnowledgeSkillPointBudget ?? state.KnowledgeSkillPointBudget",
            "await Task.Run(() => Coordinator.PreviewCreationSkills(",
            "new CreationSkillsPreviewPage(",
            'AutomationId = "creation-skills-preview-page"',
            'AutomationId = "creation-skills-confirm"',
            "Coordinator.ConfirmCreationSkillsAsync(",
            'AutomationId = "creation-skills-confirm-receipt"',
            '"SkillsPreview.DurablePendingFinalization"',
        ):
            self.assertIn(marker, page)

        for marker in (
            "CreationSkillsPhoneAuthority.IsReady(state, overview)",
            "CreationSkillsPhoneAuthority.BindingEquals(_binding, state.Binding)",
            "foreach (CharacterCreationSkillProjection item in state.Skills)",
            "foreach (CharacterCreationSkillGroupProjection item in state.SkillGroups)",
            "_availableSkills.Contains((source.Kind, source.SourceSkillId))",
            "_availableGroups.Contains(source.GroupId)",
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
            "CharacterCreationSkillsAccessRules.IsSkillAvailable(",
            "CharacterCreationSkillsAccessRules.IsGroupAvailable(",
        ):
            self.assertIn(marker, authority)

        for marker in (
            "IOwnerBoundCharacterCreationSkillsService? _ownerBoundSkillsService",
            "LoadCreationSkills()",
            "PreviewCreationSkills(",
            "ConfirmCreationSkillsAsync(",
            "service.Confirm(owner, new(canonical.Binding, allocations, groups,",
            "canonical.PreviewDigest, idempotencyKey, true",
            "service.Load(owner, new(receipt.WorkspaceId))",
            "issued.Load.Display.DisplayOwnerContext is not { } owner",
            "IOwnerBoundWorkspaceRefreshPresenter bound",
            "bound.LoadAsync(owner, receipt.WorkspaceId",
            "CreationSkillsPhoneAuthority.ReceiptMatches(",
            "CreationSkillsPhoneConfirmResult NeedsReopen()",
            "CharacterCreationSkillsBlockers.PostCommitRefreshRequired",
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
            '"Magical Active"',
            '"Resonance Active"',
            '"Aspected Magician"',
            "unlockskills",
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

    def test_rating_preview_retains_controls_only_after_current_core_acceptance(self) -> None:
        page = (NATIVE / "CreationSkillsPage.cs").read_text(encoding="utf-8")
        rating = page[page.index("    private void BindRatingAdjustment("):
                      page.index("    private async Task PreviewAsync(")]
        for marker in ("RunWithConditionalRefreshAsync", "render == _renderGeneration",
                       "IsCurrentAppearanceGeneration(appearance)",
                       "Coordinator.ReadCreationAuthority(original,", "lifetime.Token.ThrowIfCancellationRequested()",
                       "if (!Current()) return false;", "_draft.TryAdopt(",
                       "!adopted || !sameBlockers || !_ratingShapeChecks.All(check => check())",
                       'button.Text = "…"', "control.IsEnabled = false"):
            self.assertIn(marker, rating)
        self.assertLess(rating.index("_draft.TryAdopt("), rating.index("foreach (var update"))
        self.assertNotIn("ConfirmCreationSkills", rating)
        self.assertNotIn("error.Message", rating)
        self.assertIn("_ratingPreparation?.Cancel();", page)

    def test_incomplete_selection_is_local_only_and_explained_before_catalogs(self) -> None:
        page = (NATIVE / "CreationSkillsPage.cs").read_text(encoding="utf-8")
        draft = (NATIVE / "CreationSkillsPhoneDraft.cs").read_text(encoding="utf-8")
        self.assertIn("CreationSkillsPhoneAuthority.CanStagePreview(", draft)
        self.assertIn("CreationSkillsPhoneAuthority.CanAdoptPreview(", page)
        self.assertIn("CreationSkillsPhoneAuthority.CanConfirmPreview(", page)
        self.assertLess(page.index("if (_blockers.Count > 0) AddBlockers(_blockers)"),
                        page.index("        AddCatalog("))
        # The page delegates blocker copy to the shared localization mapper.
        self.assertIn("blockers.Select(CreationAllocationStrings.SkillBlocker)", page)
        self.assertIn('Get("Skills.NativeLanguageRequired",',
                      (NATIVE / "CreationAllocationStrings.cs").read_text(encoding="utf-8"))

    def test_confirmation_reprojects_then_validates_receipt_before_activation(self) -> None:
        coordinator = (NATIVE / "RunnerSessionCoordinator.CreationSkills.cs").read_text(
            encoding="utf-8"
        )
        confirmation = coordinator[
            coordinator.index("private async Task<CreationSkillsPhoneConfirmResult> ConfirmIssuedSkillsAsync(") :
        ]

        preview_index = confirmation.index("service.Preview(owner,")
        equality_index = confirmation.index("CreationSkillsPhoneAuthority.CanonicallyEquals(")
        confirm_index = confirmation.index("service.Confirm(owner,")
        direct_load_index = confirmation.index(
            "service.Load(owner, new(receipt.WorkspaceId))"
        )
        receipt_index = confirmation.index(
            "CreationSkillsPhoneAuthority.ReceiptMatchesBeforeActivation("
        )
        presenter_load_index = confirmation.index("bound.LoadAsync(owner, receipt.WorkspaceId")
        shell_index = confirmation.index("SyncShellAsync(ct)")

        self.assertLess(preview_index, equality_index)
        self.assertLess(equality_index, confirm_index)
        self.assertLess(confirm_index, direct_load_index)
        self.assertLess(direct_load_index, receipt_index)
        self.assertLess(receipt_index, presenter_load_index)
        self.assertLess(presenter_load_index, shell_index)
        self.assertIn("CharacterCreationSkillsBlockers.PostCommitRefreshRequired", confirmation)
        self.assertIn("issued.ConfirmationStarted = true", confirmation)
        self.assertIn("IsCreationSkillsPreviewCurrent(preview)", confirmation)
        self.assertIn("IsNativePersistenceOwnerCurrent(owner)", confirmation)
        self.assertNotIn("_creationSkillsService", coordinator)

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
