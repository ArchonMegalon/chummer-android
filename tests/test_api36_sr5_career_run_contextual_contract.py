import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


class Sr5CareerRunContextualContractTests(unittest.TestCase):
    def test_table_load_preserves_ui_context_for_visual_refresh(self) -> None:
        table = (ROOT / "src/Chummer.Android/Native/Sr5TableWizardPage.cs").read_text(
            encoding="utf-8"
        )
        load_method = table.split(
            "private async Task LoadLatestAsync(CancellationToken cancellationToken)", 1
        )[1].split("private static bool MatchesCurrent", 1)[0]

        self.assertIn("await _authority\n                .LoadAsync(_lane, cancellationToken);", load_method)
        self.assertNotIn("ConfigureAwait(false)", load_method)
        self.assertIn("finally", load_method)
        self.assertIn("_loading = false;\n                Refresh();", load_method)

    def test_before_run_driver_semantics_are_typed_but_not_release_claimed(self) -> None:
        shell = (ROOT / "src/Chummer.Android/Native/PhoneShellPages.cs").read_text(
            encoding="utf-8"
        )
        table = (ROOT / "src/Chummer.Android/Native/Sr5TableWizardPage.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn('automationId: "phone-table-before-run"', shell)
        self.assertIn("Sr5TableWizardLane.BeforeRun", shell)
        self.assertIn('"sr5-table-action-"', table)
        self.assertIn('"sr5-table-wizard-resume-review"', table)
        self.assertIn('"sr5-table-wizard-boundary"', table)

        inventory = json.loads(
            (
                ROOT
                / "docs/ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json"
            ).read_text(encoding="utf-8")
        )
        recognition = inventory["generationInputs"]["api36JourneyRecognition"]
        self.assertEqual("recognized", recognition["recognitionStatus"])
        self.assertEqual("not_executed", recognition["executionStatus"])
        self.assertEqual(
            [
                {
                    "route": "sr5-career/before-run",
                    "matrixJourney": "before-run-edge",
                    "gateStatus": "required",
                    "executionStatus": "not_executed",
                },
                {
                    "route": "sr5-career/playtime",
                    "matrixJourney": "playtime-short-burst",
                    "gateStatus": "required",
                    "executionStatus": "not_executed",
                },
            ],
            recognition["matrixJourneys"],
        )
        self.assertFalse(recognition["releaseClaim"])
        self.assertEqual(0, recognition["completionCountContribution"])

        workflow = (ROOT / ".github/workflows/api36-editing-e2e.yml").read_text(
            encoding="utf-8"
        )
        runner = (ROOT / "scripts/run-api36-editing-e2e-ci.sh").read_text(
            encoding="utf-8"
        )
        self.assertNotIn("phone-table-before-run", workflow)
        self.assertNotIn("phone-table-before-run", runner)
        self.assertIn("before-run-edge", workflow)
        self.assertIn("before-run-edge", runner)
        self.assertIn("playtime-short-burst", workflow)
        self.assertIn("playtime-short-burst", runner)
        self.assertIn("downtime-calendar", workflow)
        self.assertIn("downtime-calendar", runner)
        self.assertIn("after-run-settlement", workflow)
        self.assertIn("after-run-settlement", runner)

        after_run = inventory["generationInputs"]["afterRunSettlementJourneyRecognition"]
        self.assertEqual("sr5-after-run-settlement", after_run["journeyId"])
        self.assertEqual(
            [
                {
                    "route": "sr5-career/after-run/settlement",
                    "matrixJourney": "after-run-settlement",
                    "gateStatus": "required",
                    "executionStatus": "not_executed",
                }
            ],
            after_run["matrixJourneys"],
        )
        self.assertFalse(after_run["releaseClaim"])

    def test_contextual_scope_is_explicit_and_generic_mutation_is_absent(self) -> None:
        catalog = (
            ROOT / "src/Chummer.Android/Native/Sr5CareerRunCapabilityCatalog.cs"
        ).read_text(encoding="utf-8")
        for capability_id in (
            "before-run-edge",
            "before-run-loadout",
            "before-run-preparation",
            "before-run-contacts",
            "before-run-commitments",
            "after-run-karma",
            "after-run-nuyen",
            "after-run-heat",
            "after-run-notoriety",
            "after-run-public-awareness",
            "after-run-contacts",
            "after-run-injuries",
            "after-run-ammo",
            "after-run-loot",
            "after-run-expenses",
            "after-run-log",
        ):
            self.assertIn(f'"{capability_id}"', catalog)
        self.assertIn("Sr5CareerRunCapabilityStatus.ReadOnly", catalog)
        self.assertIn("no typed Core/Presentation mutation authority", catalog)
        self.assertIn("LabelKey", catalog)
        self.assertIn("AuthorityKey", catalog)
        self.assertIn("Sr5CareerFlowStrings.Text(LabelKey)", catalog)
        self.assertIn("Sr5CareerFlowStrings.Text(AuthorityKey)", catalog)
        self.assertNotIn("Dictionary<string, object", catalog)
        self.assertNotIn("generic edit", catalog.casefold())

    def test_career_commerce_follows_review_receipt_semantics_without_repin(self) -> None:
        page = (
            ROOT / "src/Chummer.Android/Native/Sr5CareerCommercePages.cs"
        ).read_text(encoding="utf-8")
        for automation_id in (
            "career-cyberware-purchase-source-route",
            "career-cyberware-purchase-grade-route",
            "career-cyberware-purchase-update-quote",
            "career-cyberware-purchase-review-diff",
            "career-cyberware-purchase-confirm",
            "career-cyberware-purchase-receipt",
            "career-cyberware-purchase-recovery-unknown",
        ):
            self.assertIn(f'"{automation_id}"', page)
        self.assertIn("Source availability", page)
        self.assertIn("Grade availability modifier", page)
        self.assertIn("no calendar or elapsed-time mutation", page)
        self.assertNotIn("BuildSectionPage", page)
        self.assertNotIn("OpenSectionAsync", page)
        self.assertIn("sr5-career-installed-gear-unavailable", page)
        self.assertIn("sr5-career-installed-implants-unavailable", page)

        workflow = (ROOT / ".github/workflows/api36-editing-e2e.yml").read_text(
            encoding="utf-8"
        )
        self.assertNotIn("career-cyberware-purchase-review-diff", workflow)


if __name__ == "__main__":
    unittest.main()
