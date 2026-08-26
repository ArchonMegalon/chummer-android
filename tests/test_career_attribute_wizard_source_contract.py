import pathlib
import unittest
import xml.etree.ElementTree as ET


REPO = pathlib.Path(__file__).resolve().parents[1]
NATIVE = REPO / "src" / "Chummer.Android" / "Native"
PHYSICAL_DRIVER = REPO / "tests" / "run_api36_sr5_career_attribute_wizard_e2e.py"
PHYSICAL_FIXTURE = REPO / "tests" / "fixtures" / "career-attribute-advance-e2e.chum5"


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
        self.assertIn("_mutationOwners.AcquireExecutionLeaseAsync", store)
        self.assertIn("_mutationOwners.TryComplete", store)
        self.assertNotIn("ApplyingMutationGate", store)
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
            "SerializedCheckpointRetainsNestedTypedAuthority",
            "PhysicalDriverGoldenIdempotencyMatchesManagedAuthority",
            "PhysicalDriverGoldenReceiptDigestMatchesCoreAuthority",
            "CoordinatorVerifiesOnlyFreshExactReceiptAsync",
            "ApplyingCrashResolvesWithoutReplayAsync",
            "DurableSharedCareerMutationOwnerSurvivesStoreRestart",
            "CheckpointCasRejectsForgedResolutionAndWrongOwner",
        ):
            self.assertIn(marker, harness)

    def test_physical_driver_binds_attribute_routes_nested_journal_and_three_restarts(self) -> None:
        driver = PHYSICAL_DRIVER.read_text(encoding="utf-8")
        for marker in (
            'CHECKPOINT_KEY = "sr5.career.attribute.draft.v1"',
            'CHOOSE_ROUTE = "sr5-career/advancement/attribute/choose"',
            'REVIEW_ROUTE = "sr5-career/advancement/attribute/review"',
            'RECEIPT_ROUTE = "sr5-career/advancement/attribute/receipt"',
            '"sr5-career-action-attribute"',
            '"sr5-career-attribute-review"',
            '"sr5-career-attribute-resume"',
            '"sr5-career-attribute-apply"',
            '"sr5-career-attribute-receipt-acknowledge"',
            'require_object_fields(checkpoint["Draft"], DRAFT_FIELDS',
            "expected_idempotency_key(checkpoint)",
            "expected_receipt_digest(checkpoint)",
            "require_same_action(reviewed.payload, applied.payload)",
            '"status": "device-pass-non-release"',
            '"releaseEvidenceStatus": "ineligible-unverified-build-provenance"',
        ):
            self.assertIn(marker, driver)
        self.assertEqual(3, driver.count("shared.force_stop_and_launch_new_process"))

    def test_physical_fixture_is_one_exact_bod_successor_case(self) -> None:
        root = ET.parse(PHYSICAL_FIXTURE).getroot()
        self.assertEqual("CareerAttributeAdvanceE2E", root.findtext("alias"))
        self.assertEqual("35", root.findtext("karma"))
        attributes = root.findall("./attributes/attribute")
        self.assertEqual(
            [
                "BOD", "AGI", "REA", "STR", "CHA", "INT",
                "LOG", "WIL", "EDG", "MAG", "MAGAdept", "RES",
            ],
            [attribute.findtext("name") for attribute in attributes],
        )
        target = attributes[0]
        expected = {
            "name": "BOD",
            "base": "0",
            "karma": "1",
            "metatypemin": "1",
            "metatypemax": "6",
            "metatypeaugmax": "9",
            "totalvalue": "2",
            "notes": "target-attribute-must-survive",
        }
        self.assertEqual(expected, {name: target.findtext(name) for name in expected})


if __name__ == "__main__":
    unittest.main()
