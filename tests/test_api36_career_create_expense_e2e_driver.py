import ast
import importlib.util
import json
from pathlib import Path
import unittest
from unittest.mock import patch

from tests.test_api36_roster_sort_e2e_driver import _resolve_repository_sibling


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests/run_api36_career_create_expense_e2e.py"
INVENTORY_SCRIPT = REPO / "scripts/materialize_chummer5_editability_inventory.py"
PRESENTATION = _resolve_repository_sibling(
    REPO.parent, "presentation", ("presentation", "chummer-presentation")
)
CORE = _resolve_repository_sibling(REPO.parent, "core", ("core", "chummer-core-engine"))
CHUMMER5 = Path("/docker/chummer5a")
CONTROLS = {
    "nudAmount": "career-create-expense-amount",
    "txtDescription": "career-create-expense-description",
    "cmdOK": "career-create-expense-ok",
    "chkRefund": "career-create-expense-refund",
    "datDate": "career-create-expense-date",
    "nudPercent": "career-create-expense-percent",
    "chkKarmaNuyenExchange": "career-create-expense-exchange",
    "chkForceCareerVisible": "career-create-expense-force-career-visible",
}


class Api36CareerCreateExpenseDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_api36_digest_bound_and_not_a_receipt(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        for control in CONTROLS:
            self.assertIn(f'"CreateExpense.{control}"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('abi != "arm64-v8a"', source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('"journey": "career-create-expense"', source)
        self.assertIn("LEGACY_REVISION", source)
        self.assertIn("LEGACY_SOURCE_DIGESTS", source)
        self.assertIn("LEGACY_METHOD_DIGESTS", source)
        self.assertIn('"careerCreateExpenseRulesSha256"', source)
        self.assertIn('"presenterPersistenceSha256"', source)
        self.assertIn('"workspaceStoreSha256"', source)
        self.assertIn("NuyenExchangeCanonicalNoOp", source)
        self.assertNotIn('"profile": "tablet"', source)

    def test_source_graph_has_typed_operation_cas_atomic_save_and_exact_no_op(self) -> None:
        page = (REPO / "src/Chummer.Android/Native/CareerCreateExpensePage.cs").read_text(
            encoding="utf-8"
        )
        build = (REPO / "src/Chummer.Android/Native/BuildPage.cs").read_text(encoding="utf-8")
        coordinator = (
            REPO / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs"
        ).read_text(encoding="utf-8")
        request = (
            PRESENTATION / "Chummer.Presentation/Overview/CareerCreateExpenseEditRequest.cs"
        ).read_text(encoding="utf-8")
        mutation = (
            PRESENTATION / "Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs"
        ).read_text(encoding="utf-8")
        presenter = (
            PRESENTATION
            / "Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs"
        ).read_text(encoding="utf-8")
        persistence = (
            PRESENTATION / "Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs"
        ).read_text(encoding="utf-8")
        rules = (
            CORE / "Chummer.Contracts/Characters/CharacterCareerCreateExpenseRules.cs"
        ).read_text(encoding="utf-8")
        store = (
            CORE / "Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs"
        ).read_text(encoding="utf-8")

        self.assertIn('automationId: "build-career-create-expense"', build)
        for automation_id in CONTROLS.values():
            self.assertIn(f'"{automation_id}"', page)
        for operation in ("KarmaGained", "KarmaSpent", "NuyenGained", "NuyenSpent"):
            self.assertIn(f"CharacterCareerCreateExpenseOperation.{operation}", page + rules)
        self.assertIn("CharacterCareerCreateExpenseState ExpectedState", request)
        self.assertIn("ExpectedContentRevision", request + coordinator + presenter)
        self.assertIn("ApplyCareerCreateExpenseEdit", mutation + presenter)
        self.assertIn("NuyenExchangeValidationRejected", page + rules + mutation)
        self.assertIn("NuyenExchangeCanonicalNoOp", page + rules + mutation)
        self.assertIn("do not save", page)
        self.assertIn("TryBeginCaptureIntent", persistence)
        self.assertIn("Flush(flushToDisk: true)", store)
        self.assertIn("File.Replace", store)

    def test_exact_canonical_control_handler_caller_and_save_load_authority_is_fail_closed(self) -> None:
        spec = importlib.util.spec_from_file_location("inventory_create_expense", INVENTORY_SCRIPT)
        assert spec is not None and spec.loader is not None
        inventory = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(inventory)

        self.assertTrue(inventory._create_expense_legacy_authority(CHUMMER5))
        authority = inventory.CREATE_EXPENSE_LEGACY_AUTHORITY
        drifted = {
            **authority,
            "methodDigests": {
                **authority["methodDigests"],
                ("Chummer/Forms/Creation Forms/CreateExpense.cs", "cmdOK_Click"): "0" * 64,
            },
        }
        with patch.object(inventory, "CREATE_EXPENSE_LEGACY_AUTHORITY", drifted):
            self.assertFalse(inventory._create_expense_legacy_authority(CHUMMER5))

    def test_inventory_maps_exact_eight_controls_phone_only_and_scripted(self) -> None:
        payload = json.loads(
            (REPO / "docs/ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json").read_text(
                encoding="utf-8"
            )
        )
        rows = {
            row["legacy"]["controlName"]: row
            for row in payload["rows"]
            if row["legacy"]["formOrControl"] == "CreateExpense"
            and row["legacy"]["controlName"] in CONTROLS
        }
        self.assertEqual(set(CONTROLS), set(rows))
        for control, automation_id in CONTROLS.items():
            row = rows[control]
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual("Build > Runner > Create expense > operation", row["phone"]["route"])
            self.assertEqual("CareerCreateExpensePage", row["phone"]["surface"])
            self.assertEqual(automation_id, row["phone"]["automationId"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_career_create_expense_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertIn("Integral Nuyen exchange", row["phone"]["coverageLimit"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("missing", row["e2e"]["tablet"]["status"])
            self.assertFalse(row["completionProven"])


if __name__ == "__main__":
    unittest.main()
