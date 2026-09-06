import importlib.util
import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
SCRIPT = REPO / "scripts" / "materialize_android_chummer5_exhaustive_parity_gap.py"
OUTPUT = REPO / "docs" / "ANDROID_CHUMMER5_EXHAUSTIVE_PARITY_GAP.generated.json"
TWO_GREEN = Path(os.environ.get(
    "CHUMMER_ANDROID_TWO_GREEN_RECEIPT",
    "/docker/chummercomplete/_completion/chummer-preview12/release-authority/two-green/"
    "ANDROID_API36_TWO_GREEN_ELIGIBILITY.generated.json",
))
SPEC = importlib.util.spec_from_file_location("android_chummer5_exhaustive_parity_gap", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
module = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(module)


class AndroidChummer5ExhaustiveParityGapTests(unittest.TestCase):
    def test_row_classification_is_fail_closed(self) -> None:
        base = {
            "legacy": {"mutationDisposition": "mutating", "controlName": "control"},
            "editParityRequired": True,
            "mutationFamily": "gear",
            "phone": {"status": "missing"},
            "tablet": {"status": "missing"},
            "completionProven": False,
        }
        self.assertEqual("missing", module.classify_row(base, set()))
        self.assertEqual(
            "implemented_unproven",
            module.classify_row({**base, "phone": {"status": "partial_exact_saved_data"}}, set()),
        )
        self.assertEqual(
            "typed_and_api36_proven",
            module.classify_row({**base, "completionProven": True}, set()),
        )
        setting = {
            **base,
            "mutationFamily": "character_settings",
            "legacy": {
                "mutationDisposition": "mutating",
                "controlName": "control",
                "formOrControl": "EditCharacterSettings",
            },
        }
        self.assertEqual("hidden_or_deferred", module.classify_row(setting, {"control"}))
        non_mutating = {
            **base,
            "legacy": {"mutationDisposition": "non_mutating", "controlName": "control"},
            "editParityRequired": False,
        }
        self.assertEqual("not_applicable_non_mutating", module.classify_row(non_mutating, set()))

    def test_unknown_inventory_status_fails_closed(self) -> None:
        row = {
            "id": "row",
            "legacy": {"mutationDisposition": "mutating", "controlName": "control"},
            "mutationFamily": "gear",
            "operation": "set_value",
            "phone": {"status": "future_magic"},
            "tablet": {"status": "missing"},
            "presenterMutation": "SetValue",
            "persistenceAssertion": "workspace revision changes",
            "e2e": {"phone": {"status": "missing"}, "tablet": {"status": "missing"}},
            "editParityRequired": True,
            "legacyReviewComplete": True,
            "overallStatus": "partial",
            "completionProven": False,
        }
        with self.assertRaisesRegex(ValueError, "unknown phone status"):
            module.validate_row(row)

    def test_mutating_row_cannot_be_excluded_from_edit_parity(self) -> None:
        row = {
            "id": "false-non-mutating",
            "legacy": {"mutationDisposition": "mutating", "controlName": "control"},
            "mutationFamily": "gear",
            "operation": "set_value",
            "phone": {"status": "missing"},
            "tablet": {"status": "missing"},
            "presenterMutation": None,
            "persistenceAssertion": None,
            "e2e": {"phone": {"status": "missing"}, "tablet": {"status": "missing"}},
            "editParityRequired": False,
            "legacyReviewComplete": True,
            "overallStatus": "missing",
            "completionProven": False,
        }
        with self.assertRaisesRegex(ValueError, "contradicts legacy mutation disposition"):
            module.validate_row(row)

    def test_inventory_summary_cannot_hide_dropped_non_mutating_rows(self) -> None:
        inventory = {
            "rows": [
                {"legacy": {"mutationDisposition": "mutating"}},
            ],
            "summary": {
                "rowCount": 2,
                "editParityRequiredCount": 1,
                "reviewedNonMutatingCount": 1,
            },
        }
        with self.assertRaisesRegex(ValueError, "row count is inconsistent"):
            module.validate_inventory_summary(inventory)

    def test_completed_mutating_row_requires_reachable_durable_two_lane_authority(self) -> None:
        row = {
            "id": "false-complete",
            "legacy": {"mutationDisposition": "mutating", "controlName": "control"},
            "mutationFamily": "gear",
            "operation": "set_value",
            "phone": {"status": "missing"},
            "tablet": {"status": "missing"},
            "presenterMutation": None,
            "persistenceAssertion": None,
            "e2e": {
                "phone": {"status": "pass", "ref": "phone.json"},
                "tablet": {"status": "pass", "ref": "tablet.json"},
            },
            "editParityRequired": True,
            "legacyReviewComplete": True,
            "overallStatus": "complete",
            "completionProven": True,
        }
        with self.assertRaisesRegex(ValueError, "without two-lane executed proof"):
            module.validate_row(row)

    @unittest.skipUnless(TWO_GREEN.is_file(), "exact Preview12 two-green receipt is not mounted")
    def test_tampered_two_green_aggregate_fails_closed(self) -> None:
        receipt = json.loads(TWO_GREEN.read_text(encoding="utf-8"))
        receipt["reviewRun"]["aggregateStatus"] = "fail"
        with self.assertRaisesRegex(ValueError, "reviewRun aggregate is not pass"):
            module.validate_two_green(
                receipt,
                "388425aceac266e06265e4c0c73a4058b052d316",
                "175da843cfc2df3489d87dc153c186b9c8e4d803",
                [row[0] for row in module.QUALIFIED_FEATURES],
            )

    @unittest.skipUnless(TWO_GREEN.is_file(), "exact Preview12 two-green receipt is not mounted")
    def test_reused_review_run_cannot_satisfy_main_authority(self) -> None:
        receipt = json.loads(TWO_GREEN.read_text(encoding="utf-8"))
        receipt["mainRun"] = receipt["reviewRun"]
        with self.assertRaisesRegex(ValueError, "run IDs are not distinct"):
            module.validate_two_green(
                receipt,
                "388425aceac266e06265e4c0c73a4058b052d316",
                "175da843cfc2df3489d87dc153c186b9c8e4d803",
                [row[0] for row in module.QUALIFIED_FEATURES],
            )

    def test_checked_in_receipt_is_row_complete_and_fail_closed(self) -> None:
        payload = json.loads(OUTPUT.read_text(encoding="utf-8"))
        self.assertEqual("incomplete_fail_closed", payload["status"])
        self.assertEqual(1751, payload["summary"]["dataChangingControlCount"])
        self.assertEqual(2229, payload["summary"]["legacyControlCount"])
        self.assertEqual(1751, len(payload["rows"]))
        self.assertEqual(0, payload["summary"]["typedAndApi36ProvenRowCount"])
        self.assertEqual(133, payload["summary"]["hiddenOrDeferredRowCount"])
        self.assertEqual(7, len(payload["qualifiedWizardFeatureSlices"]))
        self.assertFalse(payload["summary"]["exhaustiveParityComplete"])
        self.assertFalse(payload["currentReleaseBoundary"]["publicationAuthorized"])
        self.assertEqual(
            "c08557be75fb76395ec94fdab78651097c8d538db8844a151e3d8cb0a30401e9",
            payload["currentReleaseBoundary"]["twoGreenReceiptSha256"],
        )
        self.assertEqual(34032852374, payload["currentReleaseBoundary"]["reviewRunId"])
        self.assertEqual(34035361631, payload["currentReleaseBoundary"]["mainRunId"])
        classified = sum(
            payload["summary"][key]
            for key in (
                "typedAndApi36ProvenRowCount",
                "implementedUnprovenRowCount",
                "hiddenOrDeferredRowCount",
                "missingRowCount",
            )
        )
        self.assertEqual(1751, classified)
        self.assertEqual(
            "missing",
            next(
                row["classification"]
                for row in payload["featureSlices"]
                if row["id"] == "cyberware.modular_limbs_descendants_bioware"
            ),
        )
        self.assertEqual(
            "missing",
            next(
                row["classification"]
                for row in payload["featureSlices"]
                if row["id"] == "vehicle_mods_and_mounts.full_customization"
            ),
        )

    @unittest.skipUnless(TWO_GREEN.is_file(), "exact Preview12 two-green receipt is not mounted")
    def test_checked_in_receipt_rebuilds_from_exact_mounted_two_green(self) -> None:
        subprocess.run(
            [
                "python3",
                str(SCRIPT),
                "--two-green-receipt",
                str(TWO_GREEN),
                "--qualified-commit",
                "388425aceac266e06265e4c0c73a4058b052d316",
                "--check",
            ],
            cwd=REPO,
            check=True,
        )

    @unittest.skipUnless(TWO_GREEN.is_file(), "exact Preview12 two-green receipt is not mounted")
    def test_materialization_is_deterministic(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            first = Path(temporary) / "first.json"
            second = Path(temporary) / "second.json"
            command = [
                "python3",
                str(SCRIPT),
                "--two-green-receipt",
                str(TWO_GREEN),
                "--qualified-commit",
                "388425aceac266e06265e4c0c73a4058b052d316",
            ]
            subprocess.run([*command, "--output", str(first)], cwd=REPO, check=True)
            subprocess.run([*command, "--output", str(second)], cwd=REPO, check=True)
            self.assertEqual(first.read_bytes(), second.read_bytes())


if __name__ == "__main__":
    unittest.main()
