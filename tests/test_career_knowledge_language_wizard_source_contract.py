import pathlib
import unittest
import xml.etree.ElementTree as ET


REPO = pathlib.Path(__file__).resolve().parents[1]
NATIVE = REPO / "src" / "Chummer.Android" / "Native"
DRIVER = REPO / "tests" / "run_api36_sr5_career_knowledge_language_wizard_e2e.py"
FIXTURE = REPO / "tests" / "fixtures" / "career-knowledge-language-advance-e2e.chum5"


class CareerKnowledgeLanguageWizardSourceContractTests(unittest.TestCase):
    def read(self, name: str) -> str:
        return (NATIVE / name).read_text(encoding="utf-8")

    def test_typed_knowledge_identity_costs_and_all_cas_dimensions_are_bound(self) -> None:
        model = self.read("Sr5CareerKnowledgeSkillWizardModel.cs")
        shared = self.read("Sr5CareerWizardModel.cs")
        for marker in (
            "CharacterCareerKnowledgeSkillAdvanceQuote Quote",
            "CharacterCareerKnowledgeSkillAdvancePlan Plan",
            "Quote.CharacterRevision",
            "Quote.LogicalRevision",
            "Quote.SourceRevision",
            "Quote.RuleDigest",
            'SourceSkillId?.ToString("D") ?? "custom"',
            "CareerKnowledgeSkillAdvanceRequest",
        ):
            self.assertIn(marker, model)
        self.assertIn("FromKnowledgeSkill", shared)
        self.assertIn("ComputeKnowledgeSkillIdempotencyKey", shared)
        self.assertIn("Sr5CareerActionKind.KnowledgeSkillAdvance", shared)
        for marker in (
            "ActiveSkillAdvance = 0",
            "AttributeAdvance = 1",
            "SkillGroupAdvance = 2",
            "QualityTransaction = 3",
            "KnowledgeSkillAdvance = 4",
        ):
            self.assertIn(marker, shared)

    def test_choose_review_apply_receipt_are_distinct_phone_steps(self) -> None:
        page = self.read("Sr5CareerKnowledgeSkillWizardPage.cs")
        routes = self.read("Sr5CareerWizardModel.cs")
        for route in ("KnowledgeSkillChoose", "KnowledgeSkillReview", "KnowledgeSkillReceipt"):
            self.assertIn(route, routes)
            self.assertIn(f"Sr5CareerWizardRoutes.{route}", page)
        for page_type in (
            "class Sr5CareerKnowledgeSkillWizardPage",
            "class Sr5CareerKnowledgeSkillReviewPage",
            "class Sr5CareerKnowledgeSkillReceiptPage",
        ):
            self.assertIn(page_type, page)
        choose, review = page.split("public sealed class Sr5CareerKnowledgeSkillReviewPage", 1)
        self.assertNotIn("_authority.ApplyAsync", choose)
        self.assertIn("_authority.ApplyAsync", review)
        self.assertIn("Apply and verify once", review)

    def test_native_language_and_knowledge_specific_blockers_stay_specialized(self) -> None:
        model = self.read("Sr5CareerKnowledgeSkillWizardModel.cs")
        page = self.read("Sr5CareerKnowledgeSkillWizardPage.cs")
        for blocker in (
            "NotCareerCharacter",
            "UnsupportedRuleset",
            "NotKnowledgeSkill",
            "ForeignIdentity",
            "UpgradeDisallowed",
            "NativeLanguage",
            "AtMaximum",
            "InsufficientKarma",
        ):
            self.assertIn(f"CharacterCareerKnowledgeSkillAdvanceBlocker.{blocker}", model)
        self.assertIn("A native language has no Karma rating to advance.", model)
        self.assertIn("_selected is { CanAdvance: true }", page)
        self.assertIn("CharacterCareerKnowledgeSkillAdvanceRules.IsCoherent(_selected)", page)

    def test_atomic_save_receipt_and_crash_recovery_fail_closed(self) -> None:
        runner = self.read("RunnerSessionCoordinator.cs")
        coordinator = self.read("Sr5CareerKnowledgeSkillCoordinator.cs")
        store = self.read("Sr5CareerKnowledgeSkillCheckpointStore.cs")
        for marker in (
            "PrepareCareerKnowledgeSkillAdvanceAsync",
            "ApplyCareerKnowledgeSkillAdvanceAsync",
            "CharacterCareerKnowledgeSkillAdvanceRules.TryPlanAdvance",
            "CharacterCareerKnowledgeSkillAdvanceRules.TryCreateReceipt",
            "preparedReceipt == expectedReceipt",
            "State.ContentRevision == request.ExpectedContentRevision + 1",
            "State.SavedRevision == appliedContentRevision",
        ):
            self.assertIn(marker, runner)
        for marker in (
            "editor.RecoverableReceipts",
            "ReceiptMatchesDraft",
            "QuotesMatchExactly",
            "Do not replay or clear it",
            "CryptographicOperations.FixedTimeEquals",
        ):
            self.assertIn(marker, coordinator)
        for marker in (
            "Sr5CareerCheckpointPhase.Reviewed",
            "Sr5CareerCheckpointPhase.Applying",
            "Sr5CareerCheckpointPhase.Applied",
            "TryBeginApply",
            "TryRecordAuthoritativeResolution",
            "TryDeleteApplied",
            "replay-blocking lock",
            "DurablyEquivalent(current, checkpoint)",
            "Sr5CareerMutationOwnerStore",
            "MutationOwnerForNextApplying",
            "MutationOwnerFromApplying",
            "TryReconcileResolvedOwner",
            "Sr5CareerMutationDomains.KnowledgeSkillAdvance",
            "AcquireExecutionLeaseAsync",
            "TryComplete",
            "TryRunWhenUnowned",
        ):
            self.assertIn(marker, store)
        self.assertNotIn("ApplyingMutationGate", store)
        owner = self.read("Sr5CareerMutationOwnerStore.cs")
        self.assertIn('KnowledgeSkillAdvance = "knowledge-skill-advance"', owner)
        self.assertIn('StorageKey = "sr5.career.mutation-owner.v1"', owner)

    def test_build_and_career_surfaces_enter_the_wizard(self) -> None:
        career = self.read("Sr5CareerWizardPage.cs")
        build = self.read("BuildPage.cs")
        for source in (career, build):
            self.assertIn("Sr5CareerKnowledgeSkillCoordinator", source)
            self.assertIn("new Sr5CareerKnowledgeSkillWizardPage", source)
        self.assertIn("OpenKnowledgeSkillWizardAsync", career)
        self.assertIn('automationId: "build-career-knowledge-language"', build)

    def test_managed_harness_covers_identity_native_language_cas_and_recovery(self) -> None:
        harness = (
            REPO / "tests" / "Chummer.Android.Sr5CareerKnowledgeSkill.Tests" / "Program.cs"
        ).read_text(encoding="utf-8")
        for marker in (
            "DraftKeepsKnowledgeIdentityAndAllCasDimensions",
            "ActionKindNumericIdentityIsStable",
            "NativeLanguageCannotCreateReviewedDraft",
            "DurableCheckpointCasMovesReviewedApplyingApplied",
            "DurableOwnerSurvivesRestartAndReconcilesResolvedJournal",
            "LegacyApplyingWithoutSharedOwnerFailsClosed",
            "MalformedSharedOwnerRemainsReplayBlocking",
            "RecoveryDistinguishesAppliedNotAppliedAndUnknown",
            "MalformedCheckpointRemainsReplayBlocking",
        ):
            self.assertIn(marker, harness)

    def test_fixture_is_one_custom_non_native_knowledge_successor_case(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        self.assertEqual("CareerKnowledgeLanguageAdvanceE2E", root.findtext("alias"))
        self.assertEqual("50", root.findtext("karma"))
        rows = root.findall("./newskills/knoskills/skill")
        self.assertEqual(1, len(rows))
        target = rows[0]
        expected = {
            "guid": "22222222-2222-2222-2222-222222222222",
            "suid": "00000000-0000-0000-0000-000000000000",
            "isknowledge": "True",
            "name": "Matrix Security",
            "type": "Academic",
            "skillcategory": "Academic",
            "isnativelanguage": "False",
            "karma": "2",
            "base": "1",
            "notes": "custom-knowledge-target-must-survive",
        }
        self.assertEqual(expected, {name: target.findtext(name) for name in expected})

    def test_physical_driver_never_relabels_hosted_x86_as_physical(self) -> None:
        driver = DRIVER.read_text(encoding="utf-8")
        for marker in (
            'CHECKPOINT_KEY = "sr5.career.knowledge-language.draft.v1"',
            'MUTATION_OWNER_KEY = "sr5.career.mutation-owner.v1"',
            'CHOOSE_ROUTE = "sr5-career/advancement/knowledge-language/choose"',
            'REVIEW_ROUTE = "sr5-career/advancement/knowledge-language/review"',
            'RECEIPT_ROUTE = "sr5-career/advancement/knowledge-language/receipt"',
            '"sr5-career-action-knowledge-language"',
            '"sr5-career-knowledge-skill-review"',
            '"sr5-career-knowledge-skill-apply"',
            '"sr5-career-knowledge-skill-receipt-acknowledge"',
            'if abi != "arm64-v8a"',
            'qemu == "1"',
            '"hostedX86Claim": False',
            '"executionStatus": "pass"',
            'action.get("Kind") != 4',
            "Resolved Knowledge journal retained its shared mutation owner",
        ):
            self.assertIn(marker, driver)


if __name__ == "__main__":
    unittest.main()
