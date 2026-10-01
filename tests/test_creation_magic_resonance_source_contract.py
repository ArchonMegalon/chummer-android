import ast
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
NATIVE = REPO / "src" / "Chummer.Android" / "Native"


class CreationMagicResonanceSourceContractTests(unittest.TestCase):
    def test_save_feedback_has_short_actions_and_translated_detail(self) -> None:
        for locale in ("", ".de", ".es"):
            path = REPO / "src/Chummer.Android/Resources/Localization" / f"CreationFlowStrings{locale}.resx"
            values = {row.attrib["name"]: row.findtext("value") for row in ET.parse(path).getroot().findall("data")}
            for key in ("Magic.Review.Confirm", "Magic.Review.Saving"):
                self.assertTrue(values[key], (locale, key))
                self.assertLessEqual(len(values[key]), 20, (locale, key))
            self.assertTrue(values["Magic.Review.Confirming"], locale)
            self.assertNotEqual(values["Magic.Review.Saving"], values["Magic.Review.Confirming"], locale)

    def test_core_reads_and_previews_are_async_and_render_uses_prepared_snapshot(self) -> None:
        page = (NATIVE / "CreationMagicResonancePage.cs").read_text(encoding="utf-8")
        refresh = page[page.index("protected override void Refresh()") : page.index("internal static bool HasUnsupportedSeparateMagicProfile")]
        self.assertNotIn("Coordinator.LoadCreationMagicResonance", refresh)
        self.assertNotIn("TryProject(", refresh)
        self.assertNotIn("_draft.Matches(", refresh)
        self.assertIn("Coordinator.IsCreationCatalogDisplayCurrent(original)", refresh)
        self.assertNotIn("Coordinator.ReviewCreationMagicResonance(", page)
        self.assertNotIn("Coordinator.LoadCreationMagicResonance()", page)
        self.assertIn("IsCurrentAppearanceGeneration(generation)", page)
        self.assertIn("_draft.TryAdoptPrepared(before, prepared)", page)
        coordinator = (NATIVE / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        worker = coordinator[coordinator.index("private Task<T> WithCreationMagicReadAsync<T>") : coordinator.index("internal CharacterCreationMagicResonanceReview ReviewCreationMagicResonance(")]
        for marker in ("WithWorkspaceActivationGateAsync", "Task.Run(", "T result = read();", "token.ThrowIfCancellationRequested();"):
            self.assertIn(marker, worker)
        self.assertGreaterEqual(worker.count("IsCreationCatalogDisplayCurrent(original)"), 2)
        self.assertNotIn("owners.TryAcquire", worker)  # Core companion owns its synchronous lease.
        self.assertIn("_ownerBoundMagicResonanceService.Load(owner, new(workspaceId))", coordinator)
        self.assertIn("new DisplayBoundMagicResonanceService(_ownerBoundMagicResonanceService, owner)", coordinator)
        self.assertNotIn("_creationMagicResonanceService.Load", coordinator)

    def test_receipt_acknowledgement_returns_through_attached_phone_shell(self) -> None:
        page = (NATIVE / "CreationMagicResonancePage.cs").read_text(encoding="utf-8")
        action = page[page.index("private async Task AcknowledgeAsync()") :]
        action = action[:action.index("private static void AddDigest(")]
        self.assertIn("_store.TryAcknowledgeConfirmed(", action)
        self.assertIn("MainShell { UsesTabletComposition: false } shell", action)
        self.assertIn("await shell.GoToAsync(PhoneShellRoutes.RunnerAbsolute, animate: false)", action)
        self.assertNotIn("Navigation.Pop", action)

    def test_shared_magic_copy_does_not_mislabel_sum_to_ten_as_priority(self) -> None:
        keys = (
            "Magic.DraftEyebrow", "Magic.Catalog.Eyebrow", "Magic.Option.Eyebrow",
            "Magic.Review.Eyebrow", "Magic.Receipt.Eyebrow", "Magic.Intro",
            "Magic.NotAllowed", "Magic.Talent.ReadOnly",
        )
        for locale in ("", ".de", ".es"):
            path = REPO / "src/Chummer.Android/Resources/Localization" / f"CreationFlowStrings{locale}.resx"
            values = {row.attrib["name"]: row.findtext("value") for row in ET.parse(path).getroot().findall("data")}
            for key in keys:
                self.assertTrue(values[key], (locale, key))
                self.assertNotIn("prior", values[key].lower(), (locale, key))

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
            "Coordinator.LoadCreationMagicResonanceForDisplayAsync(",
            "Coordinator.ReviewCreationMagicResonanceForDisplayAsync(",
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
            "acknowledge.IsEnabled = !receipt.CharacterDocumentChanged",
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

    def test_magic_save_diagnostics_are_disclosed_not_primary_content(self) -> None:
        page = (NATIVE / "CreationMagicResonancePage.cs").read_text(encoding="utf-8")
        receipt = page[page.index("public sealed class CreationMagicResonanceReceiptPage") :]
        for key in ("Common.PreviousRevision", "Common.ContentRevision", "Common.SavedRevision",
                    "Common.DraftRevision", "Magic.Receipt.IdempotentReplay",
                    "Magic.Receipt.CurrentDraft", "Common.DocumentChanged"):
            self.assertIn(f'diagnostics.Add(NativeTheme.Metric(\n            CreationFlowStrings.Get("{key}"', receipt)
        self.assertIn('NativeTheme.TechnicalDetails(diagnostics, "creation-magic-resonance-receipt-details")', receipt)
        self.assertIn("CreationMagicResonancePage.KindLabel(receipt.TalentKind)", receipt)
        self.assertIn("acknowledge.IsEnabled = !receipt.CharacterDocumentChanged", receipt)
        self.assertIn("Coordinator.IsCreationMagicOwnerCurrent(_originalOwner)", receipt)
        self.assertIn("Coordinator.State.WorkspaceId == _confirmation.Receipt.WorkspaceId", receipt)
        self.assertIn("if (!CanDisplayReceipt() || !_checkpoint.OwnsRecoveryRevision(Coordinator.State)) return;", receipt)
        rereview = (NATIVE / "CreationMagicReReviewPage.cs").read_text(encoding="utf-8")
        self.assertNotIn("_body.Add(identity)", rereview)
        self.assertNotIn("_body.Add(binding)", rereview)
        self.assertIn('NativeTheme.TechnicalDetails(identity, "creation-magic-rereview-receipt-details")', rereview)
        self.assertIn('NativeTheme.TechnicalDetails(binding, "creation-magic-rereview-details")', rereview)

    def test_magic_primary_copy_is_plain_language_in_all_supported_locales(self) -> None:
        keys = ("Magic.DraftIncomplete", "Magic.ExactBudgets", "Magic.FinalizationBoundary",
                "Magic.Receipt.Safe", "Magic.Review.Boundary", "Magic.Review.NoIdentities",
                "Magic.Recovery.Confirmed", "Magic.Recovery.Confirming", "Magic.Recovery.Reviewed",
                "Magic.Recovery.Stale")
        for locale in ("", ".de", ".es"):
            path = REPO / "src/Chummer.Android/Resources/Localization" / f"CreationFlowStrings{locale}.resx"
            values = {row.attrib["name"]: row.findtext("value") for row in ET.parse(path).getroot().findall("data")}
            for key in keys:
                self.assertTrue(values[key], (locale, key))
                for diagnostic in ("Core", "XML", "CharacterDocumentChanged", "digest", "idempoten", "Hilfszustand", "auxiliary"):
                    self.assertNotIn(diagnostic.casefold(), values[key].casefold(), (locale, key))

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
            "IOwnerBoundCharacterCreationMagicResonanceService? _ownerBoundMagicResonanceService",
            "LoadCreationMagicResonance(CharacterOverviewState? display",
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

    def test_magic_checkpoint_and_confirmation_keep_original_owner(self) -> None:
        checkpoint = (NATIVE / "CreationMagicResonanceCheckpointStore.cs").read_text(encoding="utf-8")
        page = (NATIVE / "CreationMagicResonancePage.cs").read_text(encoding="utf-8")
        self.assertIn("isCurrent?.Invoke(original) != true", checkpoint)
        self.assertIn('StorageKey + ".owner."', checkpoint)
        self.assertIn("Preferences.Default.Get(WorkspaceKey()", checkpoint)
        self.assertIn("string ownerKey = Key();", checkpoint)
        self.assertIn('ownerKey + ".workspace."', checkpoint)
        self.assertIn("coordinator.State.DisplayOwnerContext, coordinator.IsCreationMagicOwnerCurrent", page)
        self.assertIn("coordinator.State.WorkspaceId?.Value", page)
        self.assertIn("CharacterOverviewState original = _display", page)
        self.assertIn("ConfirmCreationMagicResonanceAsync(confirming, display: original)", page)
        self.assertIn("ResolveConfirmingAsync(checkpoint, original)", page)
        self.assertIn("ConfirmCreationMagicResonanceAsync(checkpoint, display: original)", page)
        preview = page[page.index("private async Task<CharacterCreationMagicResonanceReview> PreviewDraftAsync(") : page.index("private void AddBinding(")]
        self.assertIn("_loadedDisplay is not { } original", preview)
        self.assertIn("ReferenceEquals(editor, _editor)", preview)
        self.assertNotIn("original = Coordinator.State", preview)
        option = page[page.index("private async Task AdoptAsync(") : page.index("public sealed class CreationMagicResonanceReviewPage")]
        self.assertIn("_display is not { } original", option)
        self.assertNotIn("original = Coordinator.State", option)

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

    def test_dashboard_magic_entry_uses_exact_typed_missing_draft_policy(self) -> None:
        dashboard = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")
        # Continue shares the stage-card route table since PR216; it no longer
        # duplicates this predicate in a second magicResonanceStep branch.
        self.assertIn(
            "canOpenMagicResonance = magicResonanceStage && BuildPageUiProjection.CanOpenExactTypedCreationStage(\n"
            "                stage, CharacterCreationWizardStepIds.MagicResonance, readiness.MagicResonance)",
            dashboard,
        )
        self.assertIn(": routes.GetValueOrDefault(stepId);", dashboard)
        self.assertIn("route?.CanOpen == true ? route.Open : static () => Task.CompletedTask", dashboard)
        self.assertIn(
            "CharacterCreationWizardStepIds.MagicResonance => CharacterCreationFinalizationBlockers.MagicResonanceDraftRequired",
            dashboard,
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
