import pathlib
import unittest


REPO = pathlib.Path(__file__).resolve().parents[1]
NATIVE = REPO / "src" / "Chummer.Android" / "Native"


class CareerAttributeWizardSourceContractTests(unittest.TestCase):
    def read(self, name: str) -> str:
        return (NATIVE / name).read_text(encoding="utf-8")

    def test_typed_quote_plan_and_all_revision_digests_are_bound(self) -> None:
        model = self.read("Sr5CareerAttributeWizardModel.cs")
        shared = self.read("Sr5CareerWizardModel.cs")
        for marker in (
            "CharacterCareerAttributeAdvanceQuote Quote",
            "CharacterCareerAttributeAdvancePlan Plan",
            "CharacterCareerAttributeAdvanceRules.IsCoherent(Quote)",
            "CharacterCareerAttributeAdvanceRules.TryPlanAdvance",
            "Quote.LogicalRevision",
            "Quote.SourceRevision",
            "Quote.RuleDigest",
            "CareerAttributeAdvanceRequest",
        ):
            self.assertIn(marker, model)
        self.assertIn("FromAttribute", shared)
        self.assertIn("ComputeAttributeIdempotencyKey", shared)
        self.assertIn("Sr5CareerActionKind.AttributeAdvance", shared)

    def test_preview_is_a_distinct_phone_navigation_step(self) -> None:
        page = self.read("Sr5CareerAttributeWizardPage.cs")
        model = self.read("Sr5CareerWizardModel.cs")
        for route in (
            "AttributeChoose",
            "AttributeReview",
            "AttributeReceipt",
        ):
            self.assertIn(route, model)
            self.assertIn(f"Sr5CareerWizardRoutes.{route}", page)
        self.assertIn("class Sr5CareerAttributeWizardPage", page)
        self.assertIn("class Sr5CareerAttributeReviewPage", page)
        self.assertIn("class Sr5CareerAttributeReceiptPage", page)
        self.assertIn("Review exact diff", page)
        self.assertIn("Apply and verify once", page)
        choose_section, review_section = page.split(
            "public sealed class Sr5CareerAttributeReviewPage", maxsplit=1
        )
        self.assertNotIn("ApplyAndSaveAsync", choose_section)
        self.assertIn("_authority.ApplyAsync", review_section)

    def test_core_blockers_are_visible_and_fail_closed(self) -> None:
        model = self.read("Sr5CareerAttributeWizardModel.cs")
        page = self.read("Sr5CareerAttributeWizardPage.cs")
        for blocker in (
            "NotCareerCharacter",
            "UnsupportedRuleset",
            "ForeignTarget",
            "SpecialAttributeDisabled",
            "AtNaturalMaximum",
            "InsufficientKarma",
        ):
            self.assertIn(f"CharacterCareerAttributeAdvanceBlocker.{blocker}", model)
        self.assertIn("_selected is { CanAdvance: true }", page)
        self.assertIn("CharacterCareerAttributeAdvanceRules.IsCoherent(_selected)", page)
        self.assertIn("OmittedAttributeCount", page)
        self.assertIn("OmittedReceiptCount", page)

    def test_atomic_save_and_fresh_receipt_recovery_are_required(self) -> None:
        runner = self.read("RunnerSessionCoordinator.cs")
        coordinator = self.read("Sr5CareerAttributeCoordinator.cs")
        for marker in (
            "PrepareCareerAttributeAdvanceAsync",
            "ApplyCareerAttributeAdvanceAsync",
            "CharacterCareerAttributeAdvanceRules.TryPlanAdvance",
            "CharacterCareerAttributeAdvanceRules.TryCreateReceipt",
            "preparedReceipt == expectedReceipt",
            "State.ContentRevision == request.ExpectedContentRevision + 1",
            "await _presenter.SaveAsync",
            "State.SavedRevision == appliedContentRevision",
            "!State.IsDirty",
        ):
            self.assertIn(marker, runner)
        self.assertIn("presenter.LoadAttributesAsync", coordinator)
        self.assertIn("editor.RecoverableReceipts", coordinator)
        self.assertIn("ReceiptMatchesDraft", coordinator)
        self.assertIn("QuotesMatchExactly(matchingQuotes[0], checkpoint.Draft.Quote)", coordinator)
        self.assertIn("JsonSerializer.Serialize(current)", coordinator)
        self.assertIn("JsonSerializer.Serialize(reviewed)", coordinator)
        self.assertIn("Do not replay or clear it", coordinator)

    def test_restart_checkpoint_is_cas_bound_and_malformed_data_remains_a_lock(self) -> None:
        store = self.read("Sr5CareerAttributeCheckpointStore.cs")
        coordinator = self.read("Sr5CareerAttributeCoordinator.cs")
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
        self.assertIn("Sr5CareerAttributeRecoveryProof.Verifies", store)
        self.assertIn("AcquireDurableApplyingLeaseAsync", store)
        self.assertIn("ApplyingMutationGate", store)
        self.assertIn("DurablyEquivalent(current, checkpoint)", store)
        self.assertNotIn("current != checkpoint", store)
        self.assertIn("JsonSerializer.Serialize(left)", store)
        self.assertIn("JsonSerializer.Serialize(right)", store)
        self.assertIn("CryptographicOperations.FixedTimeEquals", coordinator)
        self.assertIn("checkpointStore.AcquireDurableApplyingLeaseAsync", coordinator)
        page = self.read("Sr5CareerAttributeWizardPage.cs")
        appearing = page.split("protected override async void OnAppearing()", maxsplit=1)[1]
        appearing = appearing.split("protected override void Refresh()", maxsplit=1)[0]
        self.assertIn("LoadRecoveryCheckpoint();", appearing)
        self.assertLess(
            appearing.index("LoadRecoveryCheckpoint();"),
            appearing.index("ResolveCheckpointAsync"),
        )

    def test_career_and_build_surfaces_enter_the_phone_wizard(self) -> None:
        career = self.read("Sr5CareerWizardPage.cs")
        build = self.read("BuildPage.cs")
        for source in (career, build):
            self.assertIn("Sr5CareerAttributeCoordinator", source)
            self.assertIn("new Sr5CareerAttributeWizardPage", source)
        self.assertIn("OpenAttributeWizardAsync", career)
        self.assertGreaterEqual(career.count('"attribute");'), 2)
        self.assertIn('automationId: "build-career-attribute"', build)

    def test_behavioral_authority_harness_covers_restart_tampering_and_blockers(self) -> None:
        harness = (
            REPO
            / "tests"
            / "Chummer.Android.Sr5CareerAttribute.Tests"
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
