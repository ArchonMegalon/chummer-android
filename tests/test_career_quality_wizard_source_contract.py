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
ATOMIC_WORKSPACE = (NATIVE / "AndroidCareerQualityAtomicWorkspace.cs").read_text(
    encoding="utf-8"
)
MAUI_PROGRAM = (REPO / "src" / "Chummer.Android" / "MauiProgram.cs").read_text(
    encoding="utf-8"
)
POLISH_AUDIT = (REPO / "docs" / "SR5_CAREER_QUALITY_POLISH_AUDIT.md").read_text(
    encoding="utf-8"
)


class CareerQualityWizardSourceContractTests(unittest.TestCase):
    def test_polish_keeps_shared_shells_reusable_and_sr5_quality_truth_typed(self) -> None:
        for token in (
            "Sr5CareerActionPlan",
            "InternalId + SourceId",
            "positive/negative type",
            "enabled-source and GM gates",
            "Mentor Spirit Way free-cost eligibility is a typed definition projection",
            "intentionally does not define duplicate guard or gate",
        ):
            self.assertIn(token, POLISH_AUDIT)
        quality_sources = MODEL + COORDINATOR + STORE + ATOMIC_WORKSPACE
        self.assertNotIn("class Sr5CareerRunnerGuard", quality_sources)
        self.assertNotIn("class Sr5CareerMutationGate", quality_sources)

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
        self.assertEqual(core.group(1), "2fb2ae9bb48e5a1a6b25a174ba88008ce995fcd5")
        self.assertEqual(presentation.group(1), "fad57e99c772450c5aea3c4dc6315d18dca65637")
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

    def test_production_registration_uses_only_the_typed_client_capability(self) -> None:
        self.assertIn(
            "AddSingleton<ICareerQualityAtomicWorkspace,\n"
            "            AndroidCareerQualityAtomicWorkspace>()",
            MAUI_PROGRAM,
        )
        self.assertIn("IChummerClient _client", ATOMIC_WORKSPACE)
        self.assertIn("_client as ICareerQualityAtomicWorkspace", ATOMIC_WORKSPACE)
        self.assertNotIn("ReplaceWorkspaceDocumentAsync", ATOMIC_WORKSPACE)
        self.assertNotIn("XDocument", ATOMIC_WORKSPACE)
        self.assertNotIn("XElement", ATOMIC_WORKSPACE)
        self.assertNotIn("Definition.Name ==", ATOMIC_WORKSPACE)

    def test_registration_seam_reloads_and_replans_before_one_atomic_call(self) -> None:
        for token in (
            "ReadRequiredAsync(",
            "RequireExpectedBinding(",
            "ResolveExactQuote(",
            "ExpectedWorkspaceRevision",
            "ExpectedSavedRevision",
            "ExpectedRuntimeFingerprint",
            "ExpectedContentDigest",
            "ExpectedLogicalRevision",
            "ExpectedSourceRevision",
            "ExpectedRuleDigest",
            "CareerQualityWorkflow.PlanConfirmation(",
            "CareerQualityWorkflow.PlanCorrection(",
            "CareerQualityWorkflow.ValidateAtomicCommit(",
            "CareerQualityWorkflow.ValidateAtomicCorrection(",
        ):
            self.assertIn(token, ATOMIC_WORKSPACE)
        self.assertEqual(ATOMIC_WORKSPACE.count(".CommitAsync(plan, ct)"), 1)
        self.assertEqual(ATOMIC_WORKSPACE.count(".CorrectAsync(correction, ct)"), 1)
        self.assertNotIn("catch (", ATOMIC_WORKSPACE)

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
            manifest["coreRevision"], "2fb2ae9bb48e5a1a6b25a174ba88008ce995fcd5"
        )
        self.assertEqual(manifest["bundleDigest"], "7a108fe4e18340166c1ae206191e7b132e5d04656e24bc2dcd71da263892ebff")
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
        self.assertIn("AndroidCareerQualityAtomicWorkspace.cs", project)


if __name__ == "__main__":
    unittest.main()
