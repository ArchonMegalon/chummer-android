import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
NATIVE = REPO / "src" / "Chummer.Android" / "Native"


class CreationQualitiesSourceContractTests(unittest.TestCase):
    def test_phone_journey_is_purpose_built_and_core_bound(self) -> None:
        page = (NATIVE / "CreationQualitiesPage.cs").read_text(encoding="utf-8")
        for marker in (
            'AutomationId = "creation-qualities-page"',
            'AutomationId = "creation-quality-configure-page"',
            'AutomationId = "creation-qualities-review-page"',
            'AutomationId = "creation-qualities-receipt-page"',
            "Coordinator.LoadCreationQualities()",
            "Coordinator.PreviewCreationQualities(",
            "CharacterCreationQualitiesDesktopOption",
            "option.OptionId",
            "option.DisableReasonKey",
            "option.SourceAnchorIds",
            "preview.PositiveQualityBudget",
            "preview.NegativeQualityBudget",
            "preview.KarmaRemaining",
            "CharacterCreationQualitiesCheckpoint.CreateReviewed(",
            "TryBeginApply(",
            "Coordinator.ConfirmCreationQualitiesAsync(",
            "TryRecordApplied(",
            'AutomationId = "creation-qualities-confirm-receipt"',
            "CharacterDocumentChanged",
            "pending whole-build finalization",
        ):
            self.assertIn(marker, page)

        for forbidden in (
            "System.Xml",
            "XmlDocument",
            "XDocument",
            "XElement",
            "QualityLevelRequest",
            "ApplyQualityEdit",
            "CharacterCreationFoundationApply",
            "CharacterCreated = true",
            "KarmaCost =",
            "MaximumSelections =",
        ):
            self.assertNotIn(forbidden, page)

    def test_android_validates_projection_and_never_invents_rules(self) -> None:
        authority = (NATIVE / "CreationQualitiesPhoneAuthority.cs").read_text(
            encoding="utf-8"
        )
        draft = (NATIVE / "CreationQualitiesPhoneDraft.cs").read_text(
            encoding="utf-8"
        )
        for marker in (
            "CharacterCreationQualitiesRules.ComputeStateDigest(state)",
            "CharacterCreationQualitiesRules.ComputeAuthorityDigest(authority)",
            "CharacterCreationQualitiesRules.ComputeOptionDigest(option)",
            "CharacterCreationQualitiesRules.ComputeGrantDigest(grant)",
            "CharacterCreationQualitiesRules.Evaluate(new(",
            "CharacterCreationQualitiesWorkflow.Project(snapshot)",
            "CharacterCreationBuildMethods.Priority",
            "option.EligibilityIsExact",
            "option.DisableReasonKey",
            "CharacterCreationQualitiesRules.TryPlan(",
            "CharacterCreationQualitiesRules.IsValidReceipt(",
            "!receipt.CharacterDocumentChanged",
            "!draft.CharacterEffectsApplied",
        ):
            self.assertIn(marker, authority)
        for marker in (
            "HashSet<string>",
            "CreationQualitiesPhoneAuthority.BindingEquals(",
            "state.PendingDraft?.SelectedOptionIds",
            "WithToggle(CharacterCreationQualitiesDesktopOption option)",
            "Coordinator",
        ):
            if marker == "Coordinator":
                self.assertNotIn(marker, draft)
            else:
                self.assertIn(marker, draft)
        combined = authority + draft
        for forbidden in (
            "System.Xml",
            "KarmaCost +",
            "KarmaCost -",
            "MayExceedPositiveQualityLimit =",
            "MayExceedNegativeQualityLimit =",
            "IsSelectable = true",
        ):
            self.assertNotIn(forbidden, combined)

    def test_checkpoint_is_durable_cas_and_malformed_state_locks_replay(self) -> None:
        checkpoint = (NATIVE / "CreationQualitiesCheckpointStore.cs").read_text(
            encoding="utf-8"
        )
        for marker in (
            "CharacterCreationQualitiesCheckpointPhase.Reviewed",
            "CharacterCreationQualitiesCheckpointPhase.Applying",
            "CharacterCreationQualitiesCheckpointPhase.Applied",
            "CheckpointDigest",
            "ComputeDigest(",
            "TryWriteAndReadBackLocked(",
            "TryRequireCasLocked(",
            "TryBeginApply(",
            "TryReturnToReviewed(",
            "TryRecordApplied(",
            "TryAcknowledgeApplied(",
            "A malformed quality checkpoint blocks replay",
            "CharacterCreationQualitiesRules.IsValidReceipt(",
            "Preferences.Default",
        ):
            self.assertIn(marker, checkpoint)
        self.assertNotIn("catch\n        {\n            _backend.Remove", checkpoint)

    def test_session_reprojects_before_atomic_core_confirmation(self) -> None:
        coordinator = (NATIVE / "RunnerSessionCoordinator.cs").read_text(
            encoding="utf-8"
        )
        for marker in (
            "ICharacterCreationQualitiesService? _creationQualitiesService",
            "LoadCreationQualities()",
            "PreviewCreationQualities(",
            "ConfirmCreationQualitiesAsync(",
            "checkpoint.OwnsRecoveryRevision(beforeActivation)",
            "_creationQualitiesService.Confirm(new(",
            "checkpoint.IdempotencyKey",
            "checkpoint.TransactionId",
            "ExplicitlyConfirmed: true",
            "ReceiptMatchesPersistedState(",
            "_presenter.LoadAsync(receipt.WorkspaceId",
            "Character effects remain pending finalization",
        ):
            self.assertIn(marker, coordinator)
        confirmation = coordinator[
            coordinator.index("ConfirmCreationQualitiesCoreAsync(") :
        ]
        confirmation = confirmation[: confirmation.index("LoadCreationFoundation()")]
        self.assertLess(
            confirmation.index("_creationQualitiesService.Confirm(new("),
            confirmation.index("_creationQualitiesService.Load(new(receipt.WorkspaceId)"),
        )
        self.assertLess(
            confirmation.index("ReceiptMatchesPersistedState("),
            confirmation.index("_presenter.LoadAsync(receipt.WorkspaceId"),
        )

    def test_build_dashboard_routes_only_the_authoritative_quality_stage(self) -> None:
        dashboard = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")
        program = (REPO / "src" / "Chummer.Android" / "MauiProgram.cs").read_text(
            encoding="utf-8"
        )
        for marker in (
            "CharacterCreationWizardStepIds.Qualities",
            "HasAuthoritativeQualities()",
            "CreationQualitiesPhoneAuthority.IsReady(state, Coordinator.State)",
            "OpenCreationQualitiesAsync",
            "new CreationQualitiesPage(Coordinator)",
            "QualitiesStageDetail(",
        ):
            self.assertIn(marker, dashboard)
        self.assertIn("provider.GetService<ICharacterCreationQualitiesService>()", program)

    def test_physical_skeleton_cannot_emit_a_pass_without_a_fixture_run(self) -> None:
        driver = (
            REPO / "tests" / "run_api36_sr5_creation_qualities_wizard_e2e.py"
        ).read_text(encoding="utf-8")
        for marker in (
            "load_and_verify_manifest",
            '"status": "unavailable"',
            '"executionStatus": "not-run"',
            '"physicalDeviceProof": False',
            '"releaseEvidenceEligible": False',
            "sr5-priority-creation-qualities-e2e.chum5",
            "return 3",
        ):
            self.assertIn(marker, driver)
        self.assertNotIn("device-pass", driver)
        self.assertNotIn('"physicalDeviceProof": True', driver)


if __name__ == "__main__":
    unittest.main()
