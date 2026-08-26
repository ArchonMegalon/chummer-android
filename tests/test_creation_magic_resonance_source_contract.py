import ast
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
NATIVE = REPO / "src" / "Chummer.Android" / "Native"


class CreationMagicResonanceSourceContractTests(unittest.TestCase):
    def test_phone_journey_is_deep_typed_and_core_presentation_bound(self) -> None:
        page = (NATIVE / "CreationMagicResonancePage.cs").read_text(
            encoding="utf-8"
        )
        for marker in (
            'AutomationId = "creation-magic-resonance-page"',
            'AutomationId = "creation-magic-resonance-catalog-page"',
            'AutomationId = "creation-magic-resonance-option-page"',
            'AutomationId = "creation-magic-resonance-review-page"',
            'AutomationId = "creation-magic-resonance-receipt-page"',
            "Coordinator.LoadCreationMagicResonance()",
            "Coordinator.ReviewCreationMagicResonance(",
            "Coordinator.ConfirmCreationMagicResonanceAsync(",
            "CharacterCreationMagicResonanceDesktopDraft",
            "CharacterCreationMagicResonanceReview",
            "CharacterCreationMagicResonanceConfirmation",
            "option.Identity.SourceId",
            "option.PointCost",
            "option.MaximumLevels",
            "option.SourceAnchorIds",
            "option.Blockers",
            "preview.TraditionBudget",
            "preview.StreamBudget",
            "preview.AdeptPowerPointBudget",
            "preview.SpellBudget",
            "preview.ComplexFormBudget",
            "TryBeginConfirm(",
            "TryRecordConfirmed(",
            'AutomationId = "creation-magic-resonance-confirm-receipt"',
            "CharacterDocumentChanged",
            "whole-build finalization",
        ):
            self.assertIn(marker, page)

        for forbidden in (
            "System.Xml",
            "XmlDocument",
            "XDocument",
            "XElement",
            "CharacterCreationMagicResonanceService(",
            "ArtificialIntelligence =>",
            "IsEnabled = true",
            "CharacterCreated = true",
            "ApplyCharacter",
            "AI provider",
        ):
            self.assertNotIn(forbidden, page)

    def test_android_trust_boundary_delegates_rules_and_fails_closed(self) -> None:
        authority = (NATIVE / "CreationMagicResonancePhoneAuthority.cs").read_text(
            encoding="utf-8"
        )
        draft = (NATIVE / "CreationMagicResonancePhoneDraft.cs").read_text(
            encoding="utf-8"
        )
        for marker in (
            "CharacterCreationMagicResonanceWorkflow.TryProject(",
            "CharacterCreationMagicResonanceWorkflow.CreateDraft(",
            "CharacterCreationMagicResonancePresentationContract.IsSupportedTalentKind(",
            "CharacterCreationMagicResonanceDigest.Compute(",
            "option.IsEnabled",
            "option.Blockers.Count == 0",
            "option.SourceAnchorIds.Count > 0",
            "option.SourceNodeDigest",
            "receipt.CharacterDocumentChanged",
            "receipt.CustomDataInputsDigest",
            "receipt.GmPolicyDigest",
            "receipt.RuntimeDigest",
        ):
            self.assertIn(marker, authority)
        for marker in (
            "CharacterCreationMagicResonanceSelections",
            "CreateSingleCandidate(",
            "CreateToggleCandidate(",
            "CreatePowerLevelCandidate(",
            "CharacterCreationAdeptPowerAllocation",
            "CreationMagicResonancePhoneAuthority.CreateDraft(",
            "Coordinator",
        ):
            if marker == "Coordinator":
                self.assertNotIn(marker, draft)
            else:
                self.assertIn(marker, draft)
        combined = authority + draft
        for forbidden in (
            "System.Xml",
            "PointCost +",
            "PointCost -",
            "SpellBudget =",
            "ComplexFormBudget =",
            "AdeptPowerPointBudget =",
            "IsEnabled = true",
        ):
            self.assertNotIn(forbidden, combined)

    def test_checkpoint_is_durable_replay_safe_cas(self) -> None:
        checkpoint = (NATIVE / "CreationMagicResonanceCheckpointStore.cs").read_text(
            encoding="utf-8"
        )
        for marker in (
            "CharacterCreationMagicResonanceCheckpointPhase.Reviewed",
            "CharacterCreationMagicResonanceCheckpointPhase.Confirming",
            "CharacterCreationMagicResonanceCheckpointPhase.Confirmed",
            "CharacterCreationMagicResonanceReview Review",
            "CharacterCreationMagicResonanceConfirmation? Confirmation",
            "ComputeIdempotencyKey(Review)",
            "CheckpointDigest",
            "TryWriteAndReadBackLocked(",
            "TryRequireCasLocked(",
            "TryBeginConfirm(",
            "TryReturnToReviewed(",
            "TryRecordConfirmed(",
            "TryAcknowledgeConfirmed(",
            "A malformed Magic/Resonance checkpoint blocks replay",
            "Preferences.Default",
        ):
            self.assertIn(marker, checkpoint)
        self.assertNotIn("catch\n        {\n            _backend.Remove", checkpoint)

    def test_session_uses_presentation_review_and_confirmation_only(self) -> None:
        coordinator = (NATIVE / "RunnerSessionCoordinator.cs").read_text(
            encoding="utf-8"
        )
        for marker in (
            "ICharacterCreationMagicResonanceService? _creationMagicResonanceService",
            "LoadCreationMagicResonance()",
            "ReviewCreationMagicResonance(",
            "CharacterCreationMagicResonanceWorkflow.Review(",
            "ConfirmCreationMagicResonanceAsync(",
            "checkpoint.OwnsRecoveryRevision(beforeActivation)",
            "CharacterCreationMagicResonanceWorkflow.Confirm(",
            "checkpoint.IdempotencyKey",
            "explicitlyConfirmed: true",
            "ConfirmationMatches(",
            "_presenter.LoadAsync(",
            "Character effects remain pending finalization",
        ):
            self.assertIn(marker, coordinator)
        section = coordinator[
            coordinator.index("ConfirmCreationMagicResonanceCoreAsync(") :
        ]
        section = section[: section.index("LoadCreationFoundation()")]
        self.assertLess(
            section.index("CharacterCreationMagicResonanceWorkflow.Confirm("),
            section.index("_presenter.LoadAsync("),
        )

    def test_dashboard_and_state_factory_preserve_existing_routes(self) -> None:
        dashboard = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")
        program = (REPO / "src" / "Chummer.Android" / "MauiProgram.cs").read_text(
            encoding="utf-8"
        )
        for marker in (
            "CharacterCreationWizardStepIds.MagicResonance",
            "HasAuthoritativeMagicResonance()",
            "CreationMagicResonancePhoneAuthority.IsReady(",
            "OpenCreationMagicResonanceAsync",
            "new CreationMagicResonancePage(Coordinator)",
            "MagicResonanceStageDetail(",
            "HasAuthoritativeQualities()",
            "OpenCreationQualitiesAsync",
            "OpenSr5CareerSpecializationWizardAsync",
            "Sr5AfterRunSettlementCoordinator",
            'automationId: "build-career-after-run-settlement"',
        ):
            self.assertIn(marker, dashboard)
        self.assertIn(
            "provider.GetService<ICharacterCreationMagicResonanceService>()", program
        )

    def test_api36_skeleton_is_syntax_valid_and_cannot_claim_a_run(self) -> None:
        driver = REPO / "tests" / "run_api36_sr5_creation_magic_resonance_e2e.py"
        source = driver.read_text(encoding="utf-8")
        ast.parse(source)
        for marker in (
            "load_and_verify_manifest",
            '"status": "unavailable"',
            '"executionStatus": "not-run"',
            '"physicalDeviceProof": False',
            '"releaseEvidenceEligible": False',
            "sr5-priority-creation-magic-resonance-e2e.chum5",
            "return 3",
        ):
            self.assertIn(marker, source)
        self.assertNotIn("device-pass", source)
        self.assertNotIn('"physicalDeviceProof": True', source)


if __name__ == "__main__":
    unittest.main()
