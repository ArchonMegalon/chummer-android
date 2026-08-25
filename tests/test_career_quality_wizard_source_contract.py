import hashlib
import json
import re
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
NATIVE = REPO / "src" / "Chummer.Android" / "Native"
MODEL = (NATIVE / "Sr5CareerQualityWizardModel.cs").read_text(encoding="utf-8")
COORDINATOR = (NATIVE / "Sr5CareerQualityCoordinator.cs").read_text(encoding="utf-8")
STORE = (NATIVE / "Sr5CareerQualityCheckpointStore.cs").read_text(encoding="utf-8")
PAGE = (NATIVE / "Sr5CareerQualityWizardPage.cs").read_text(encoding="utf-8")
RUNNER = (NATIVE / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
CAREER = (NATIVE / "Sr5CareerWizardPage.cs").read_text(encoding="utf-8")
BUILD = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")


class CareerQualityWizardSourceContractTests(unittest.TestCase):
    def test_exact_core_presentation_content_and_runtime_generation_is_bound(self) -> None:
        core = re.search(r'CurrentCoreRevision\s*=\s*\n?\s*"([0-9a-f]{40})"', MODEL)
        presentation = re.search(
            r'CurrentPresentationRevision\s*=\s*\n?\s*"([0-9a-f]{40})"', MODEL
        )
        content = re.search(r'CurrentContentDigest\s*=\s*\n?\s*"([0-9a-f]{64})"', MODEL)
        runtime = re.search(r'CurrentRuntimeDigest\s*=\s*\n?\s*"([0-9a-f]{64})"', MODEL)
        contract = re.search(r'CurrentContractName\s*=\s*\n?\s*"([^"]+)"', MODEL)
        self.assertIsNotNone(core)
        self.assertIsNotNone(presentation)
        self.assertIsNotNone(content)
        self.assertIsNotNone(runtime)
        self.assertIsNotNone(contract)
        self.assertEqual(core.group(1), "3a0ac44854004dff0c08807d839cd1fdae1c9a65")
        self.assertEqual(presentation.group(1), "ac4ebc482c632efa2e6ecadf1df884963fc56d28")
        expected = hashlib.sha256(
            f"{contract.group(1)}\n{core.group(1)}\n{presentation.group(1)}\n{content.group(1)}\n".encode()
        ).hexdigest()
        self.assertEqual(runtime.group(1), expected)

    def test_selection_is_typed_and_never_label_identity(self) -> None:
        self.assertIn("quote.Identity.InternalId", MODEL)
        self.assertIn("quote.Identity.SourceId", MODEL)
        self.assertIn("draft.Operation != quote.Operation", MODEL)
        self.assertNotIn("ResolveByName", MODEL + COORDINATOR + PAGE)
        self.assertNotIn("Definition.Name ==", MODEL + COORDINATOR + PAGE)

    def test_full_source_effect_eligibility_and_gm_authority_fails_closed(self) -> None:
        for token in (
            "quote.Definition.SourceEnabled",
            "quote.Definition.Implemented",
            "quote.Authority.GmAllows",
            "quote.Authority.DefinitionProjectionIsExact",
            "quote.Authority.IdentityProjectionIsExact",
            "quote.Authority.Eligibility.IsExact",
            "quote.Authority.Effects.IsExact",
            "quote.Authority.Effects.UnsupportedFamilies.Count != 0",
            "editor.OmittedCandidateCount != 0",
            "editor.OmittedReceiptCount != 0",
        ):
            self.assertIn(token, MODEL + COORDINATOR)
        self.assertIn("UnsupportedEffectFamily", MODEL)
        self.assertIn("GmRestricted", MODEL)

    def test_workspace_saved_owner_rule_source_content_runtime_and_transaction_bindings(self) -> None:
        for token in (
            "ExpectedOwnerId",
            "ExpectedWorkspaceRevision",
            "ExpectedSavedRevision",
            "ExpectedRulesetId",
            "ExpectedLogicalRevision",
            "ExpectedSourceRevision",
            "ExpectedRuleDigest",
            "ExpectedRuntimeFingerprint",
            "ExpectedContentDigest",
            "TransactionId",
            "IdempotencyKey",
        ):
            self.assertIn(token, MODEL)

    def test_checkpoint_is_persistent_cas_and_unknown_outcomes_never_replay(self) -> None:
        for token in (
            "TryWriteAndReadBackLocked",
            "TryRequireCasLocked",
            "AcquireDurableApplyingLeaseAsync",
            "replay-blocking lock",
            "ApplyingMutationGate",
            "TryRecordAuthoritativeResolution",
            "Sr5CareerQualityRecoveryProof.Verifies",
        ):
            self.assertIn(token, STORE)
        self.assertIn("Never replay here", COORDINATOR)
        self.assertNotIn("ConfirmAndRefreshAsync(\n                    draft.Review", COORDINATOR.split("catch", 1)[1])

    def test_presentation_atomic_workspace_is_the_only_mutation_boundary(self) -> None:
        self.assertIn("CareerQualityInteractionPresenter", RUNNER)
        self.assertIn("ICareerQualityAtomicWorkspace", RUNNER)
        self.assertIn("ConfirmAsync", RUNNER)
        self.assertIn("CorrectAsync", RUNNER)
        combined = MODEL + COORDINATOR + STORE + PAGE
        self.assertNotIn("XDocument", combined)
        self.assertNotIn("XElement", combined)
        self.assertNotIn("ReplaceWorkspaceDocumentAsync", combined)
        self.assertNotIn("ApplyCollectionMutation", combined)

    def test_phone_deep_choose_review_receipt_and_correction_routes_are_exposed(self) -> None:
        route_model = (NATIVE / "Sr5CareerWizardModel.cs").read_text(encoding="utf-8")
        for route in (
            "sr5-career/advancement/quality/choose",
            "sr5-career/advancement/quality/review",
            "sr5-career/advancement/quality/receipt",
        ):
            self.assertIn(route, route_model)
        for automation_id in (
            "sr5-career-quality-picker",
            "sr5-career-quality-review",
            "sr5-career-quality-apply",
            "sr5-career-quality-receipt-acknowledge",
            "sr5-career-quality-receipt-correct",
            "sr5-career-quality-resolve-outcome",
        ):
            self.assertIn(automation_id, PAGE)
        self.assertIn("OpenQualityWizardAsync", CAREER)
        self.assertIn("OpenSr5CareerQualityWizardAsync", BUILD)

    def test_review_renders_prerequisites_effect_families_costs_and_all_digests(self) -> None:
        for token in (
            "quote.Prerequisites",
            "Applied effect families",
            "Unsupported effect families",
            "Rule cost / delta",
            "Logical revision",
            "Source revision",
            "Rule digest",
            "Content digest",
            "Runtime digest",
            "GM policy",
        ):
            self.assertIn(token, PAGE)

    def test_correction_is_compensating_and_receipt_bound(self) -> None:
        for token in (
            "OriginalReceipt:",
            "ExpectedReceiptDigest:",
            "CorrectionId",
            "OriginalTransactionId",
            "RestoreInstances",
            "SavedCharacterKarma",
            "ExpenseIdToRemove",
            "OriginalReceiptDigest",
        ):
            self.assertIn(token, COORDINATOR + PAGE)
        self.assertIn("TryDeleteCorrected", STORE + PAGE)

    def test_content_manifest_matches_exact_quality_core_generation(self) -> None:
        manifest = json.loads(
            (REPO / "src" / "Chummer.Android" / "Content" / "chummer-content-manifest.json")
            .read_text(encoding="utf-8")
        )
        self.assertEqual(
            manifest["coreRevision"], "3a0ac44854004dff0c08807d839cd1fdae1c9a65"
        )
        self.assertEqual(manifest["bundleDigest"], "61dddaad0bcbd80f3e8a17bfc7b875787dffb6a854fb1672b847b766dd05c0ff")
        self.assertEqual(len(manifest["files"]), 110)

    def test_focused_compile_harness_uses_exact_authority_sources(self) -> None:
        project = (
            REPO
            / "tests"
            / "Chummer.Android.Sr5CareerQuality.Tests"
            / "Chummer.Android.Sr5CareerQuality.Tests.csproj"
        ).read_text(encoding="utf-8")
        self.assertIn("CharacterCareerQualityRules.cs", project)
        self.assertIn("CareerQualityWorkflow.cs", project)
        self.assertIn("Sr5CareerQualityCoordinator.cs", project)
        self.assertIn("Sr5CareerQualityCheckpointStore.cs", project)


if __name__ == "__main__":
    unittest.main()
