import importlib.util
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


REPO = Path(__file__).resolve().parents[1]
SCRIPT = REPO / "scripts" / "materialize_chummer5_editability_inventory.py"
SPEC = importlib.util.spec_from_file_location("chummer5_editability_inventory", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
inventory = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(inventory)


class Chummer5EditabilityInventoryTests(unittest.TestCase):
    def test_parser_includes_direct_dynamic_and_event_wired_controls_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            forms = root / "Chummer" / "Forms"
            controls = root / "Chummer" / "Controls"
            forms.mkdir(parents=True)
            controls.mkdir(parents=True)
            (forms / "Demo.Designer.cs").write_text(
                """
namespace Chummer.Sample
{
    partial class Demo
    {
        private System.Windows.Forms.TextBox txtName;
        private System.Windows.Forms.Button cmdDelete;
        private System.Windows.Forms.Label lblClickable;
        private System.Windows.Forms.Label lblPlain;
        private void InitializeComponent()
        {
            this.txtName.Text = "Runner";
            this.txtName.TextChanged += new System.EventHandler(this.txtName_TextChanged);
            this.cmdDelete.Click += new System.EventHandler(this.cmdDelete_Click);
            this.lblClickable.Click += this.lblClickable_Click;
        }
    }
}
""",
                encoding="utf-8",
            )
            (forms / "Demo.cs").write_text(
                """
namespace Chummer.Sample
{
    partial class Demo
    {
        private void txtName_TextChanged(object sender, System.EventArgs e) { }
        private void cmdDelete_Click(object sender, System.EventArgs e) { }
        private void lblClickable_Click(object sender, System.EventArgs e) { }
        private void BuildRuntimeControls()
        {
            var cmdRuntimeAdd = new System.Windows.Forms.Button { Text = "Add" };
        }
    }
}
""",
                encoding="utf-8",
            )
            (controls / "Dynamic.cs").write_text(
                """
namespace Chummer.Sample
{
    partial class Dynamic
    {
        private readonly Chummer.NumericUpDownEx nudRating;
        private readonly Chummer.ButtonWithToolTip cmdImprove;
    }
}
""",
                encoding="utf-8",
            )

            rows, summary = inventory.extract_legacy_rows(root)
            by_name = {row["legacy"]["controlName"]: row for row in rows}

            self.assertEqual(
                {"txtName", "cmdDelete", "lblClickable", "cmdRuntimeAdd", "nudRating", "cmdImprove"},
                set(by_name),
            )
            self.assertEqual("direct_value_editor", by_name["txtName"]["legacy"]["candidateKind"])
            self.assertEqual("Runner", by_name["txtName"]["legacy"]["text"])
            self.assertEqual("delete", by_name["cmdDelete"]["operation"])
            self.assertEqual("definite", by_name["cmdDelete"]["legacy"]["mutationConfidence"])
            self.assertEqual("event_wired_control", by_name["lblClickable"]["legacy"]["candidateKind"])
            self.assertEqual("review_required", by_name["lblClickable"]["legacy"]["mutationConfidence"])
            self.assertEqual("runtime_action", by_name["cmdRuntimeAdd"]["legacy"]["candidateKind"])
            self.assertEqual("add", by_name["cmdRuntimeAdd"]["operation"])
            self.assertEqual("direct_value_editor", by_name["nudRating"]["legacy"]["candidateKind"])
            self.assertEqual("action", by_name["cmdImprove"]["legacy"]["candidateKind"])
            self.assertEqual(3, summary["sourceFileCount"])
            self.assertEqual(1, summary["designerFileCount"])

    def test_generated_inventory_is_row_complete_unique_and_honestly_incomplete(self) -> None:
        artifact_path = REPO / "docs" / "ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json"
        payload = json.loads(artifact_path.read_text(encoding="utf-8"))
        rows = payload["rows"]

        self.assertEqual(inventory.SCHEMA, payload["schema"])
        self.assertEqual("incomplete_fail_closed", payload["status"])
        self.assertFalse(payload["completionProven"])
        self.assertEqual(payload["summary"]["rowCount"], len(rows))
        self.assertGreater(payload["summary"]["sourceFileCount"], 200)
        self.assertGreater(payload["summary"]["designerFileCount"], 100)
        self.assertGreater(payload["summary"]["definiteMutationCandidateCount"], 1000)
        self.assertEqual(0, payload["summary"]["reviewRequiredCount"])
        self.assertEqual(len(rows), payload["summary"]["legacyReviewCompleteCount"])
        self.assertGreater(payload["summary"]["reviewedNonMutatingCount"], 0)
        self.assertEqual(0, payload["summary"]["unclassifiedCount"])
        self.assertEqual(
            payload["summary"]["reviewedNonMutatingCount"] + 74,
            payload["summary"]["completionProvenCount"],
        )
        self.assertEqual(len(rows), len({row["id"] for row in rows}))
        for row in rows:
            self.assertTrue(set(payload["requiredRowFields"]).issubset(row))
            self.assertTrue(row["legacyReviewComplete"])
            self.assertTrue(row["legacy"]["dispositionEvidence"])
            if row["completionProven"]:
                self.assertIn(
                    row["overallStatus"],
                    {"complete", "not_applicable_non_mutating"},
                )
            if not row["editParityRequired"]:
                self.assertTrue(row["completionProven"])
                self.assertEqual("not_applicable_non_mutating", row["overallStatus"])
            self.assertFalse(Path(row["legacy"]["sourcePath"]).is_absolute())
            self.assertIn(row["phone"]["status"], {
                "implemented_pending_emulator",
                "implemented_verified_api36",
                "partial_exact_saved_data",
                "partial_create_only",
                "read_only",
                "missing",
                "not_applicable_non_mutating",
            })
            self.assertIn(row["tablet"]["status"], {
                "implemented_pending_emulator",
                "implemented_verified_api36",
                "partial_exact_saved_data",
                "partial_create_only",
                "read_only",
                "missing",
                "not_applicable_non_mutating",
            })

        origin_rows = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] in inventory.ORIGIN_FIELDS
        ]
        attribute_rows = [
            row for row in rows
            if row["legacy"]["formOrControl"] == "AttributeControl"
            and row["legacy"]["controlName"] in inventory.ATTRIBUTE_FIELDS
        ]
        matrix_rows = [
            row for row in rows
            if row["legacy"]["formOrControl"] == "CharacterCareer"
            and inventory.MATRIX_CONDITION_CONTROL_RE.fullmatch(row["legacy"]["controlName"])
        ]
        character_condition_rows = [
            row for row in rows
            if row["legacy"]["formOrControl"] == "CharacterCareer"
            and inventory.CHARACTER_CONDITION_CONTROL_RE.fullmatch(row["legacy"]["controlName"])
        ]
        dashboard_condition_rows = [
            row for row in rows
            if row["legacy"]["formOrControl"] == "ConditionMonitorUserControl"
            and row["legacy"]["controlName"] in inventory.DASHBOARD_CONDITION_CONTROLS
        ]
        vehicle_physical_rows = [
            row for row in rows
            if row["legacy"]["formOrControl"] == "CharacterCareer"
            and inventory.VEHICLE_PHYSICAL_CONDITION_CONTROL_RE.fullmatch(
                row["legacy"]["controlName"]
            )
        ]
        contact_controls = (
            set(inventory.CONTACT_TEXT_FIELDS)
            | set(inventory.CONTACT_TOGGLE_FIELDS)
            | set(inventory.CONTACT_RATING_FIELDS)
            | {"cmdDelete"}
        )
        contact_rows = [
            row for row in rows
            if row["legacy"]["formOrControl"] == "ContactControl"
            and row["legacy"]["controlName"] in contact_controls
        ]
        pet_controls = set(inventory.PET_TEXT_FIELDS) | {"cmdDelete"}
        pet_rows = [
            row for row in rows
            if row["legacy"]["formOrControl"] == "PetControl"
            and row["legacy"]["controlName"] in pet_controls
        ]
        self.assertEqual(26, len(origin_rows))
        self.assertEqual(4, len(attribute_rows))
        self.assertEqual(120, len(matrix_rows))
        self.assertEqual(48, len(character_condition_rows))
        self.assertEqual(4, len(dashboard_condition_rows))
        self.assertEqual(24, len(vehicle_physical_rows))
        self.assertEqual(18, len(contact_rows))
        self.assertEqual(4, len(pet_rows))
        linked_character_rows = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"ContactControl", "PetControl"}
            and row["legacy"]["controlName"] in {"tsAttachCharacter", "tsRemoveCharacter"}
        ]
        self.assertEqual(4, len(linked_character_rows))
        self.assertTrue(all(row["presenterMutation"] for row in origin_rows + attribute_rows))
        attribute_by_name = {row["legacy"]["controlName"]: row for row in attribute_rows}
        for control_name in ("nudBase", "nudKarma"):
            row = attribute_by_name[control_name]
            self.assertEqual("implemented_verified_api36", row["phone"]["status"])
            self.assertEqual("executed_api36", row["e2e"]["phone"]["status"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("missing", row["e2e"]["tablet"]["status"])
            self.assertFalse(row["completionProven"])
        for control_name in ("cmdBurnEdge", "cmdImproveATT"):
            row = attribute_by_name[control_name]
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual("missing", row["e2e"]["phone"]["status"])
            self.assertFalse(row["completionProven"])
        self.assertTrue(all(row["presenterMutation"] for row in matrix_rows))
        condition_rows = character_condition_rows + dashboard_condition_rows + vehicle_physical_rows
        self.assertTrue(all(row["presenterMutation"] for row in condition_rows))
        self.assertTrue(
            all(row["phone"]["status"] == "partial_exact_saved_data" for row in matrix_rows)
        )
        self.assertTrue(
            all(row["tablet"]["status"] == "partial_exact_saved_data" for row in matrix_rows)
        )
        self.assertTrue(
            all(
                row["phone"]["status"] == "implemented_verified_api36"
                for row in character_condition_rows + dashboard_condition_rows
            )
        )
        self.assertTrue(
            all(
                row["tablet"]["status"] == "implemented_verified_api36"
                for row in character_condition_rows + dashboard_condition_rows
            )
        )
        self.assertTrue(
            all(
                row["phone"]["status"] == "partial_exact_saved_data"
                for row in vehicle_physical_rows
            )
        )
        self.assertTrue(
            all(
                row["tablet"]["status"] == "partial_exact_saved_data"
                for row in vehicle_physical_rows
            )
        )
        partial_rows = matrix_rows + vehicle_physical_rows
        self.assertTrue(all(row["phone"]["coverageLimit"] for row in partial_rows))
        self.assertTrue(all(row["tablet"]["coverageLimit"] for row in partial_rows))
        self.assertTrue(
            all("coverageLimit" not in row["phone"] for row in character_condition_rows + dashboard_condition_rows)
        )
        self.assertTrue(
            all("coverageLimit" not in row["tablet"] for row in character_condition_rows + dashboard_condition_rows)
        )
        self.assertTrue(all(row["persistenceAssertion"] for row in matrix_rows))
        self.assertTrue(all(row["e2e"]["phone"]["status"] == "missing" for row in matrix_rows))
        self.assertTrue(all(row["e2e"]["tablet"]["status"] == "missing" for row in matrix_rows))
        scripted_condition_rows = character_condition_rows + dashboard_condition_rows
        self.assertTrue(
            all(row["e2e"]["phone"]["status"] == "executed_api36" for row in scripted_condition_rows)
        )
        self.assertTrue(
            all(row["e2e"]["tablet"]["status"] == "executed_api36" for row in scripted_condition_rows)
        )
        self.assertTrue(all(row["completionProven"] for row in scripted_condition_rows))
        self.assertTrue(all(row["overallStatus"] == "complete" for row in scripted_condition_rows))
        self.assertTrue(all(row["e2e"]["phone"]["status"] == "missing" for row in vehicle_physical_rows))
        self.assertTrue(all(row["e2e"]["tablet"]["status"] == "missing" for row in vehicle_physical_rows))
        self.assertTrue(all(row["presenterMutation"] for row in contact_rows))
        self.assertTrue(all(row["persistenceAssertion"] for row in contact_rows))
        self.assertTrue(
            all(row["phone"]["status"] == "implemented_verified_api36" for row in contact_rows)
        )
        self.assertTrue(
            all(row["tablet"]["status"] == "implemented_verified_api36" for row in contact_rows)
        )
        self.assertTrue(
            all(
                row["e2e"]["phone"]["status"] == "executed_api36"
                and row["e2e"]["tablet"]["status"] == "executed_api36"
                and row["completionProven"]
                for row in contact_rows
            )
        )
        self.assertTrue(all(row["presenterMutation"] for row in pet_rows))
        self.assertTrue(all(row["persistenceAssertion"] for row in pet_rows))
        self.assertTrue(
            all(row["phone"]["status"] == "implemented_verified_api36" for row in pet_rows)
        )
        self.assertTrue(
            all(row["tablet"]["status"] == "implemented_verified_api36" for row in pet_rows)
        )
        self.assertTrue(
            all(
                row["e2e"]["phone"]["status"] == "executed_api36"
                and row["e2e"]["tablet"]["status"] == "executed_api36"
                and row["completionProven"]
                for row in pet_rows
            )
        )
        contact_by_name = {
            row["legacy"]["controlName"]: row
            for row in rows
            if row["legacy"]["formOrControl"] == "ContactControl"
        }
        self.assertEqual("implemented_pending_emulator", contact_by_name["tsAttachCharacter"]["phone"]["status"])
        self.assertEqual("implemented_pending_emulator", contact_by_name["tsRemoveCharacter"]["tablet"]["status"])
        self.assertEqual("not_applicable_non_mutating", contact_by_name["cmdLink"]["phone"]["status"])
        pet_by_name = {
            row["legacy"]["controlName"]: row
            for row in rows
            if row["legacy"]["formOrControl"] == "PetControl"
        }
        self.assertEqual("implemented_pending_emulator", pet_by_name["tsAttachCharacter"]["phone"]["status"])
        self.assertEqual("implemented_pending_emulator", pet_by_name["tsRemoveCharacter"]["tablet"]["status"])
        self.assertEqual("not_applicable_non_mutating", pet_by_name["cmdLink"]["phone"]["status"])
        self.assertTrue(
            all(
                row["phone"]["status"] == "implemented_pending_emulator"
                and row["tablet"]["status"] == "implemented_pending_emulator"
                and row["e2e"]["phone"]["status"] == "scripted_not_executed"
                and row["e2e"]["tablet"]["status"] == "scripted_not_executed"
                for row in linked_character_rows
            )
        )
        self.assertEqual(
            {
                "implemented_pending_emulator": 32,
                "implemented_verified_api36": 76,
                "missing": 1413,
                "not_applicable_non_mutating": 454,
                "partial_create_only": 110,
                "partial_exact_saved_data": 144,
            },
            payload["summary"]["phoneStatusCounts"],
        )
        self.assertEqual(
            {
                "implemented_pending_emulator": 4,
                "implemented_verified_api36": 74,
                "missing": 1553,
                "not_applicable_non_mutating": 454,
                "partial_exact_saved_data": 144,
            },
            payload["summary"]["tabletStatusCounts"],
        )
        mapped_ids = {
            row["id"] for row in matrix_rows + condition_rows + contact_rows + pet_rows + linked_character_rows
        }
        self.assertTrue(
            all(
                row["tablet"]["status"] == "missing"
                for row in rows
                if row["editParityRequired"] and row["id"] not in mapped_ids
            )
        )

    def test_condition_receipts_fail_closed_when_a_driver_hash_is_stale(self) -> None:
        self.assertEqual(
            {"phone", "tablet"},
            set(inventory._validated_condition_e2e_receipts()),
        )
        with tempfile.TemporaryDirectory(dir=REPO / "docs") as temporary:
            temporary_root = Path(temporary)
            receipt_paths = {}
            for profile, source in inventory.CONDITION_E2E_RECEIPTS.items():
                receipt = json.loads(source.read_text(encoding="utf-8"))
                if profile == "phone":
                    receipt["driverSha256"] = "0" * 64
                target = temporary_root / f"{profile}.json"
                target.write_text(json.dumps(receipt), encoding="utf-8")
                receipt_paths[profile] = target

            with patch.dict(inventory.CONDITION_E2E_RECEIPTS, receipt_paths, clear=True):
                self.assertEqual({}, inventory._validated_condition_e2e_receipts())

    def test_contact_pet_receipts_fail_closed_when_a_driver_hash_is_stale(self) -> None:
        self.assertEqual(
            {"phone", "tablet"},
            set(inventory._validated_contact_pet_e2e_receipts()),
        )
        with tempfile.TemporaryDirectory(dir=REPO / "docs") as temporary:
            temporary_root = Path(temporary)
            receipt_paths = {}
            for profile, source in inventory.CONTACT_PET_E2E_RECEIPTS.items():
                receipt = json.loads(source.read_text(encoding="utf-8"))
                if profile == "tablet":
                    receipt["driverSha256"] = "0" * 64
                target = temporary_root / f"{profile}.json"
                target.write_text(json.dumps(receipt), encoding="utf-8")
                receipt_paths[profile] = target

            with patch.dict(inventory.CONTACT_PET_E2E_RECEIPTS, receipt_paths, clear=True):
                self.assertEqual({}, inventory._validated_contact_pet_e2e_receipts())

    def test_attribute_receipt_is_phone_only_and_driver_hash_bound(self) -> None:
        driver = REPO / "tests" / "run_api36_attribute_e2e.py"
        shared_driver = REPO / "tests" / "run_api36_editing_e2e.py"
        receipt = {
            "schema": "chummer.android.editing-e2e/v1",
            "status": "pass",
            "profile": "phone",
            "journey": "attributes",
            "apiLevel": 36,
            "apkSha256": "a" * 64,
            "driverSha256": inventory._sha256_file(driver),
            "sharedDriverSha256": inventory._sha256_file(shared_driver),
            "journeys": {journey: "pass" for journey in inventory.ATTRIBUTE_E2E_JOURNEYS},
        }
        with tempfile.TemporaryDirectory(dir=REPO / "docs") as temporary:
            receipt_path = Path(temporary) / "receipt.json"
            receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
            with patch.object(inventory, "ATTRIBUTE_PHONE_E2E_RECEIPT", receipt_path):
                validated = inventory._validated_attribute_phone_e2e_receipt()
                self.assertIsNotNone(validated)
                assert validated is not None
                self.assertEqual("executed_api36", validated["status"])

                receipt["profile"] = "tablet"
                receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
                self.assertIsNone(inventory._validated_attribute_phone_e2e_receipt())


if __name__ == "__main__":
    unittest.main()
