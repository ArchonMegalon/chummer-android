import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
NATIVE = REPO / "src" / "Chummer.Android" / "Native"


class CreationQualitiesSourceContractTests(unittest.TestCase):
    def test_catalog_hides_diagnostics_without_changing_admission(self) -> None:
        page = (NATIVE / "CreationQualitiesPage.cs").read_text(encoding="utf-8")
        catalog = page[:page.index("public sealed class CreationQualityConfigurePage")]
        refresh = catalog[catalog.index("protected override void Refresh()"):catalog.index("private void AddBinding(")]
        self.assertIn("_technicalDetails.Clear();", refresh)
        self.assertIn("_technicalDetails.IsVisible = false;", refresh)
        self.assertLess(refresh.index("AddReview(state, checkpointOwnsLane);"),
                        refresh.index("AddTechnicalDetailsDisclosure();"))
        binding = catalog[catalog.index("private void AddBinding("):catalog.index("private void AddBudgets(")]
        self.assertIn("_technicalDetails.Add(binding);", binding)
        self.assertNotIn("_body.Add(", binding)
        digest = catalog[catalog.index("private void AddDigest("):catalog.index("private void AddTechnicalDetailsDisclosure(")]
        self.assertIn("_technicalDetails.Add(label);", digest)
        self.assertIn("label.AutomationId = automationId;", digest)
        self.assertIn("label.LineBreakMode = LineBreakMode.CharacterWrap;", digest)
        self.assertIn("if (!ReferenceEquals(toggle.Parent, _body))", catalog)
        self.assertIn("CreationQualitiesPhoneAuthority.IsOptionConfigurable(option)", catalog)
        self.assertIn("enabled: !checkpointOwnsLane", catalog)
        self.assertIn("UnavailableReason(option.DisableReasonKey)", catalog)
        self.assertIn("CreationQualitiesPage.UnavailableReason(_option.DisableReasonKey)", page)
        self.assertIn("toggle.IsEnabled = exact;", page)

    def test_catalog_readability_copy_is_translated_and_nontechnical(self) -> None:
        for locale in ("", ".de", ".es"):
            path = REPO / "src/Chummer.Android/Resources/Localization" / f"CreationFlowStrings{locale}.resx"
            rows = ET.parse(path).getroot().findall("data")
            copy = {row.attrib["name"]: row.findtext("value") for row in rows}
            self.assertEqual(len(rows), len(copy), "Duplicate localization key")
            for key in ("ShowDetails", "HideDetails", "SourceDisabled", "Unavailable",
                        "Intro", "CoreLedgers", "FinalizationBoundary", "Configure.Preview",
                        "Review.Heading", "Review.Confirm", "Review.Boundary", "Review.Empty",
                        "Receipt.PageTitle", "Receipt.Safe", "Receipt.Continue"):
                value = copy[f"Qualities.{key}"]
                self.assertTrue(value)
                self.assertNotIn("creation-qualities-", value)
                self.assertNotIn("Core", value)
                self.assertNotIn("authority", value)

    def test_quality_help_has_inline_copy_not_book_referrals(self) -> None:
        page = (NATIVE / "CreationQualitiesPage.cs").read_text(encoding="utf-8")
        help_page = page[page.index("public sealed class CreationQualityInfoPage"):
                         page.index("public sealed class CreationQualityConfigurePage")]
        for forbidden in ("Citation(", "Qualities.Info.Reference", "Qualities.Info.SourceNote",
                          "read the book", "See the rulebook", "Consult the rules"):
            self.assertNotIn(forbidden, help_page)
        keys = None
        effect_keys = None
        for locale in ("", ".de", ".es"):
            path = REPO / "src/Chummer.Android/Resources/Localization" / f"CreationFlowStrings{locale}.resx"
            copy = {row.attrib["name"]: row.findtext("value") for row in ET.parse(path).getroot().findall("data")}
            self.assertEqual(len(copy), len({key.casefold() for key in copy}),
                             "Resource compilation rejects case-only duplicate keys")
            summaries = {key for key in copy if key.startswith("Qualities.Summary.")}
            self.assertGreaterEqual(len(summaries), 126)
            if keys is not None:
                self.assertEqual(keys, summaries, "Quality summaries must be translated together")
            keys = summaries
            localized_effects = {key for key in copy if key.startswith(("Qualities.Effect.", "Qualities.Value."))}
            if effect_keys is not None:
                self.assertEqual(effect_keys, localized_effects, "Effect labels and conditions need all three locales")
            effect_keys = localized_effects
            for key, value in copy.items():
                if key.startswith(("Qualities.Info.", "Qualities.Summary.")):
                    self.assertTrue(value)
                    for referral in ("rulebook", "Regelbuch", "consulta el manual", "Consulta las reglas"):
                        self.assertNotIn(referral, value)
                if key in localized_effects:
                    self.assertTrue(value)

    def test_acknowledged_receipt_returns_through_phone_shell(self) -> None:
        page = (NATIVE / "CreationQualitiesPage.cs").read_text(encoding="utf-8")
        action = page[page.index("private async Task AcknowledgeAsync()") :]
        action = action[:action.index("private static void AddDigest(")]
        self.assertIn("await RunAsync(AcknowledgeAsync)", page)
        self.assertIn("_store.TryAcknowledgeApplied(", action)
        self.assertIn("MainShell { UsesTabletComposition: false } shell", action)
        self.assertIn("await shell.GoToAsync(PhoneShellRoutes.RunnerAbsolute, animate: false)", action)
        self.assertNotIn("Navigation.Pop", action)

    def test_catalog_render_does_not_repeat_core_or_digest_work(self) -> None:
        page = (NATIVE / "CreationQualitiesPage.cs").read_text(encoding="utf-8")
        render = page[page.index("protected override void Refresh()") : page.index("private void AddBinding(")]
        for forbidden in ("LoadCreationQualities", "IsReady(", "ProjectEditor(", "_draft.Bind(", "_draft.Matches("):
            self.assertNotIn(forbidden, render)
        self.assertIn("await Task.Run(() =>", page)
        self.assertIn("CreationQualitiesPhoneDraft draft = _draft.Copy();", page)
        self.assertIn("cancellationToken.ThrowIfCancellationRequested();", page)
        self.assertIn("Coordinator.IsCreationCatalogDisplayCurrent(original)", render)

    def test_catalog_is_bounded_and_review_precedes_it(self) -> None:
        page = (NATIVE / "CreationQualitiesPage.cs").read_text(encoding="utf-8")
        self.assertIn("matches.Skip(_catalogOffset).Take(CatalogPageSize)", page)
        self.assertLess(page.index("AddReview(state, checkpointOwnsLane);"),
                        page.index("AddOptions(state, editor, checkpointOwnsLane);"))
        self.assertIn("search.SearchButtonPressed +=", page)
        self.assertIn("ApplyFilter(search.Text)", page)
        for locale in ("", ".de", ".es"):
            resources = (REPO / "src/Chummer.Android/Resources/Localization" /
                         f"CreationFlowStrings{locale}.resx").read_text(encoding="utf-8")
            for key in ("Search", "NoMatches", "Showing", "Previous", "Next"):
                self.assertIn(f'name="Qualities.{key}"', resources)

    def test_search_refreshes_results_without_detaching_the_editor(self) -> None:
        page = (NATIVE / "CreationQualitiesPage.cs").read_text(encoding="utf-8")
        filtering = page[page.index("private void ApplyFilter("):page.index("private void AddReview(")]
        self.assertIn("RenderCatalog(state, editor, _checkpointOwnsLane)", filtering)
        self.assertNotIn("Refresh();", filtering)
        self.assertIn("IsCatalogCurrent()", filtering)
        rendering = page[page.index("private void RenderCatalog("):page.index("private void ApplyFilter(")]
        self.assertIn("_catalog.Clear();", rendering)
        self.assertNotIn("_body.Clear();", rendering)
        self.assertNotIn("new SearchBar", rendering)
        self.assertIn("ReferenceEquals(search.Parent, _body)", page)

    def test_phone_journey_is_purpose_built_and_core_bound(self) -> None:
        page = (NATIVE / "CreationQualitiesPage.cs").read_text(encoding="utf-8")
        for marker in (
            'AutomationId = "creation-qualities-page"',
            'AutomationId = "creation-quality-configure-page"',
            'AutomationId = "creation-qualities-review-page"',
            'AutomationId = "creation-qualities-receipt-page"',
            "Coordinator.LoadCreationQualitiesForDisplayAsync(_original,",
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
            "Their effects are applied when you finish creating your runner.",
        ):
            self.assertIn(marker, page)

        # Read-only source-backed help parses bounded catalog XML. Selection,
        # review and receipt pages must still delegate rules and edits to Core.
        interactions = page[page.index("public sealed class CreationQualitiesPage"):
                            page.index("public sealed class CreationQualityInfoPage")]
        interactions += page[page.index("public sealed class CreationQualityConfigurePage"):]
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
            self.assertNotIn(forbidden, interactions)

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
            "IOwnerBoundCharacterCreationQualitiesService? _ownerBoundQualitiesService",
            "LoadCreationQualities(CharacterOverviewState? display = null)",
            "PreviewCreationQualities(",
            "ConfirmCreationQualitiesAsync(",
            "checkpoint.OwnsRecoveryRevision(beforeActivation)",
            "service.Confirm(owner, new(",
            "checkpoint.IdempotencyKey",
            "checkpoint.TransactionId",
            "ExplicitlyConfirmed: true",
            "ReceiptMatchesPersistedState(",
            "bound.LoadAsync(owner, receipt.WorkspaceId",
            "Character effects remain pending finalization",
        ):
            self.assertIn(marker, coordinator)
        confirmation = coordinator[
            coordinator.index("ConfirmCreationQualitiesCoreAsync(") :
        ]
        confirmation = confirmation[: confirmation.index("LoadCreationFoundation()")]
        self.assertLess(
            confirmation.index("service.Confirm(owner, new("),
            confirmation.index("service.Load(owner, new(receipt.WorkspaceId)"),
        )
        self.assertLess(
            confirmation.index("ReceiptMatchesPersistedState("),
            confirmation.index("bound.LoadAsync(owner, receipt.WorkspaceId"),
        )
        self.assertNotIn("_creationQualitiesService", coordinator)
        self.assertIn("IsCreationCatalogDisplayCurrent(beforeActivation)", confirmation)
        self.assertIn("State.DisplayOwnerContext != owner", confirmation)

    def test_checkpoint_storage_keeps_original_owner_and_no_linked_legacy_fallback(self) -> None:
        store = (NATIVE / "CreationQualitiesCheckpointStore.cs").read_text(encoding="utf-8")
        page = (NATIVE / "CreationQualitiesPage.cs").read_text(encoding="utf-8")
        self.assertIn("isCurrent?.Invoke(original) != true", store)
        self.assertIn('StorageKey + ".owner."', store)
        self.assertIn("SHA256.HashData(", store)
        self.assertIn("owner.Owner.Value", store)
        self.assertIn("coordinator.State.DisplayOwnerContext, coordinator.IsCreationQualitiesOwnerCurrent", page)
        self.assertIn("ConfirmCreationQualitiesAsync(applying, display: _original)", page)

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
        self.assertIn("ownerBoundCreationQualitiesService: provider.GetRequiredService<IOwnerBoundCharacterCreationQualitiesService>()", program)

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
