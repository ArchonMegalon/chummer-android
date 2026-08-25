import hashlib
import json
import pathlib
import re
import unittest


REPO = pathlib.Path(__file__).resolve().parents[1]
NATIVE = REPO / "src" / "Chummer.Android" / "Native"


class CareerSkillGroupWizardSourceContractTests(unittest.TestCase):
    def read(self, name: str) -> str:
        return (NATIVE / name).read_text(encoding="utf-8")

    def test_typed_quote_plan_and_all_revision_digests_are_bound(self) -> None:
        model = self.read("Sr5CareerSkillGroupWizardModel.cs")
        shared = self.read("Sr5CareerWizardModel.cs")
        for marker in (
            "CharacterCareerSkillGroupAdvanceQuote Quote",
            "CharacterCareerSkillGroupAdvancePlan Plan",
            "CharacterCareerSkillGroupAdvanceRules.IsCoherent(Quote)",
            "CharacterCareerSkillGroupAdvanceRules.TryPlanAdvance",
            "Quote.LogicalRevision",
            "Quote.SourceRevision",
            "Quote.RuleDigest",
            "editor.RulesetId",
            "CharacterCareerSkillGroupAdvanceRules.RulesetId",
            "Sr5CareerSkillGroupRuntimeAuthority RuntimeAuthority",
            "RuntimeAuthority.ContentDigest",
            "RuntimeAuthority.RuntimeDigest",
            "CareerSkillGroupAdvanceRequest",
        ):
            self.assertIn(marker, model)
        self.assertIn("FromSkillGroup", shared)
        self.assertIn("ComputeSkillGroupIdempotencyKey", shared)
        self.assertIn("Sr5CareerActionKind.SkillGroupAdvance", shared)

    def test_content_and_runtime_authority_match_the_exact_product_graph(self) -> None:
        model = self.read("Sr5CareerSkillGroupWizardModel.cs")
        manifest = json.loads(
            (REPO / "src/Chummer.Android/Content/chummer-content-manifest.json")
            .read_text(encoding="utf-8")
        )

        def constant(name: str) -> str:
            match = re.search(rf'{name}\s*=\s*\n?\s*"([0-9a-z.\-/]+)"', model)
            self.assertIsNotNone(match, f"missing {name}")
            return match.group(1)

        contract = constant("CurrentContractName")
        core = constant("CurrentCoreRevision")
        presentation = constant("CurrentPresentationRevision")
        content = constant("CurrentContentDigest")
        runtime = constant("CurrentRuntimeDigest")
        self.assertEqual(core, manifest["coreRevision"])
        self.assertEqual(content, manifest["bundleDigest"])
        self.assertEqual(core, "b1d6abd5ea0e00c5063bc6561a87c50ec1b7eb85")
        self.assertEqual(presentation, "671289bb75994a686308cd3f3a1a52e5590f36a4")
        payload = f"{contract}\n{core}\n{presentation}\n{content}\n".encode()
        self.assertEqual(runtime, hashlib.sha256(payload).hexdigest())
        self.assertIn("contentDigest", shared := self.read("Sr5CareerWizardModel.cs"))
        self.assertIn("runtimeDigest", shared)

    def test_preview_is_a_distinct_phone_navigation_step(self) -> None:
        page = self.read("Sr5CareerSkillGroupWizardPage.cs")
        model = self.read("Sr5CareerWizardModel.cs")
        for route in (
            "SkillGroupChoose",
            "SkillGroupReview",
            "SkillGroupReceipt",
        ):
            self.assertIn(route, model)
            self.assertIn(f"Sr5CareerWizardRoutes.{route}", page)
        self.assertIn("class Sr5CareerSkillGroupWizardPage", page)
        self.assertIn("class Sr5CareerSkillGroupReviewPage", page)
        self.assertIn("class Sr5CareerSkillGroupReceiptPage", page)
        self.assertIn("Review exact diff", page)
        self.assertIn("Apply and verify once", page)
        choose_section, review_section = page.split(
            "public sealed class Sr5CareerSkillGroupReviewPage", maxsplit=1
        )
        self.assertNotIn("ApplyAndSaveAsync", choose_section)
        self.assertIn("_authority.ApplyAsync", review_section)

    def test_core_blockers_are_visible_and_fail_closed(self) -> None:
        model = self.read("Sr5CareerSkillGroupWizardModel.cs")
        page = self.read("Sr5CareerSkillGroupWizardPage.cs")
        for blocker in (
            "NotCareerCharacter",
            "UnsupportedRuleset",
            "ForeignTarget",
            "InvalidMemberProjection",
            "Broken",
            "Disabled",
            "AtMaximum",
            "InsufficientKarma",
        ):
            self.assertIn(f"CharacterCareerSkillGroupAdvanceBlocker.{blocker}", model)
        self.assertIn("_selected is { CanAdvance: true }", page)
        self.assertIn("CharacterCareerSkillGroupAdvanceRules.IsCoherent(_selected)", page)
        self.assertIn("OmittedSkillGroupCount", page)
        self.assertIn("OmittedReceiptCount", page)

    def test_atomic_save_and_fresh_receipt_recovery_are_required(self) -> None:
        runner = self.read("RunnerSessionCoordinator.cs")
        coordinator = self.read("Sr5CareerSkillGroupCoordinator.cs")
        for marker in (
            "PrepareCareerSkillGroupAdvanceAsync",
            "ApplyCareerSkillGroupAdvanceAsync",
            "request.ExpectedRulesetId",
            "CharacterCareerSkillGroupAdvanceRules.TryPlanAdvance",
            "CharacterCareerSkillGroupAdvanceRules.IsCoherent(preparedReceipt)",
            "preparedReceipt.TransactionId == expectedPlan.TransactionId",
            "State.ContentRevision == request.ExpectedContentRevision + 1",
            "await _presenter.SaveAsync",
            "State.SavedRevision == appliedContentRevision",
            "!State.IsDirty",
        ):
            self.assertIn(marker, runner)
        self.assertIn("presenter.LoadSkillGroupsAsync", coordinator)
        self.assertIn("editor.RecoverableReceipts", coordinator)
        self.assertIn("ReceiptMatchesDraft", coordinator)
        self.assertIn("Do not replay or clear it", coordinator)

    def test_restart_checkpoint_is_cas_bound_and_malformed_data_remains_a_lock(self) -> None:
        store = self.read("Sr5CareerSkillGroupCheckpointStore.cs")
        coordinator = self.read("Sr5CareerSkillGroupCoordinator.cs")
        for marker in (
            "Sr5CareerCheckpointPhase.Reviewed",
            "Sr5CareerCheckpointPhase.Applying",
            "Sr5CareerCheckpointPhase.Applied",
            "TryBeginApply",
            "TryRecordAuthoritativeResolution",
            "TryDeleteReviewed",
            "TryDeleteApplied",
            "OwnsResolution",
            "RestoreExactLocked",
            "read-back",
            "replay-blocking lock",
        ):
            self.assertIn(marker, store)
        self.assertIn("Sr5CareerSkillGroupRecoveryProof.Verifies", store)
        self.assertIn("AcquireDurableApplyingLeaseAsync", store)
        self.assertIn("ApplyingMutationGate", store)
        self.assertIn("TryDeleteCorrected", store)
        self.assertIn("CryptographicOperations.FixedTimeEquals", coordinator)
        self.assertIn("checkpointStore.AcquireDurableApplyingLeaseAsync", coordinator)

    def test_exact_member_prerequisites_time_and_compensating_correction_are_visible(self) -> None:
        page = self.read("Sr5CareerSkillGroupWizardPage.cs")
        coordinator = self.read("Sr5CareerSkillGroupCoordinator.cs")
        for marker in (
            "EnabledMemberCount",
            "CharacterCareerSkillGroupPrerequisiteResult",
            "ApplicationDuration",
            "TimeAuthority",
            "Correct this advancement",
            "TryDeleteCorrected",
        ):
            self.assertIn(marker, page)
        self.assertIn("CorrectAsync", coordinator)
        self.assertIn("CareerSkillGroupCorrectionRequest", coordinator)

    def test_career_and_build_surfaces_enter_the_phone_wizard(self) -> None:
        career = self.read("Sr5CareerWizardPage.cs")
        build = self.read("BuildPage.cs")
        for source in (career, build):
            self.assertIn("Sr5CareerSkillGroupCoordinator", source)
            self.assertIn("new Sr5CareerSkillGroupWizardPage", source)
        self.assertIn("OpenSkillGroupWizardAsync", career)
        self.assertGreaterEqual(career.count('"skill-group");'), 2)
        self.assertIn('automationId: "build-career-skill-group"', build)

    def test_behavioral_authority_harness_covers_restart_tampering_and_blockers(self) -> None:
        harness = (
            REPO
            / "tests"
            / "Chummer.Android.Sr5CareerSkillGroup.Tests"
            / "Program.cs"
        ).read_text(encoding="utf-8")
        for marker in (
            "ExactDraftBindsTypedIdentityQuotePlanAndDigests",
            "BlockedQuotesNeverBecomeDrafts",
            "CheckpointRejectsTamperingAndPriorSchemaLocks",
            "CoordinatorVerifiesOnlyFreshExactReceiptAsync",
            "ApplyingCrashResolvesWithoutReplayAsync",
            "CheckpointCasRejectsForgedResolutionAndWrongOwner",
        ):
            self.assertIn(marker, harness)


if __name__ == "__main__":
    unittest.main()
