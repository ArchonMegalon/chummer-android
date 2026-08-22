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


def _sibling_repo(canonical_name: str, coherent_name: str) -> Path:
    canonical = REPO.parent / canonical_name
    return canonical if canonical.is_dir() else REPO.parent / coherent_name


PRESENTATION_ROOT = _sibling_repo("chummer-presentation", "presentation")
CORE_ROOT = _sibling_repo("chummer-core-engine", "core")


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

    def test_orphaned_legacy_form_reopens_fail_closed_when_a_caller_appears(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            forms = root / "Chummer" / "Forms"
            controls = root / "Chummer" / "Controls"
            forms.mkdir(parents=True)
            controls.mkdir(parents=True)
            (forms / "SelectSetting.Designer.cs").write_text(
                """
namespace Chummer
{
    partial class SelectSetting
    {
        private Chummer.ElasticComboBox cboSetting;
        private System.Windows.Forms.Button cmdOK;
        private void InitializeComponent()
        {
            this.cmdOK.Click += new System.EventHandler(this.cmdOK_Click);
        }
    }
}
""",
                encoding="utf-8",
            )
            (forms / "SelectSetting.cs").write_text(
                """
namespace Chummer
{
    partial class SelectSetting
    {
        private void cmdOK_Click(object sender, System.EventArgs e) { }
    }
}
""",
                encoding="utf-8",
            )

            rows, _ = inventory.extract_legacy_rows(root)
            by_name = {row["legacy"]["controlName"]: row for row in rows}
            self.assertEqual("unreachable_legacy_form", by_name["cboSetting"]["operation"])
            self.assertEqual("unreachable_legacy_form", by_name["cmdOK"]["operation"])
            self.assertTrue(
                all(row["legacy"]["mutationDisposition"] == "non_mutating" for row in rows)
            )

            (forms / "Caller.cs").write_text(
                """
namespace Chummer
{
    partial class Caller
    {
        private void Open() { _ = new SelectSetting(); }
    }
}
""",
                encoding="utf-8",
            )
            rows, _ = inventory.extract_legacy_rows(root)
            by_name = {row["legacy"]["controlName"]: row for row in rows}
            self.assertEqual("set_value", by_name["cboSetting"]["operation"])
            self.assertEqual("commit", by_name["cmdOK"]["operation"])
            self.assertTrue(
                all(row["legacy"]["mutationDisposition"] == "mutating" for row in rows)
            )

    def test_career_karma_view_toggles_are_reviewed_non_mutating(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            forms = root / "Chummer" / "Forms"
            controls = root / "Chummer" / "Controls"
            forms.mkdir(parents=True)
            controls.mkdir(parents=True)
            (forms / "CharacterCareer.Designer.cs").write_text(
                """
namespace Chummer
{
    partial class CharacterCareer
    {
        private Chummer.ColorableCheckBox chkShowFreeKarma;
        private Chummer.ColorableCheckBox chkShowKarmaChart;
        private void InitializeComponent()
        {
            this.chkShowFreeKarma.CheckedChanged += chkShowFreeKarma_CheckedChanged;
            this.chkShowKarmaChart.CheckedChanged += chkShowKarmaChart_CheckedChanged;
        }
    }
}
""",
                encoding="utf-8",
            )
            (forms / "CharacterCareer.cs").write_text(
                """
namespace Chummer
{
    partial class CharacterCareer
    {
        private void chkShowFreeKarma_CheckedChanged(object sender, System.EventArgs e)
            => RepopulateKarmaExpenseList();
        private void chkShowKarmaChart_CheckedChanged(object sender, System.EventArgs e)
            => chtKarma.Visible = chkShowKarmaChart.Checked;
    }
}
""",
                encoding="utf-8",
            )

            rows, _ = inventory.extract_legacy_rows(root)
            by_name = {row["legacy"]["controlName"]: row for row in rows}
            self.assertEqual("filter_view", by_name["chkShowFreeKarma"]["operation"])
            self.assertEqual("toggle_view", by_name["chkShowKarmaChart"]["operation"])
            for control in ("chkShowFreeKarma", "chkShowKarmaChart"):
                self.assertEqual("non_mutating", by_name[control]["legacy"]["mutationDisposition"])
                self.assertIn("writes no runner", by_name[control]["legacy"]["dispositionEvidence"])

    def test_career_nuyen_view_toggles_are_source_guarded_non_mutating(self) -> None:
        source = (
            PRESENTATION_ROOT / "Chummer" / "Forms" / "Character Forms" / "CharacterCareer.cs"
        ).read_bytes().decode("utf-8-sig")
        expected = {
            "chkShowFreeNuyen": (
                "filter_view",
                "chkShowFreeNuyen_CheckedChanged",
                "1db5fd83dd0928161f45bc06e68d22c7d27e4c52f6d00c23945d207f0040fd1a",
            ),
            "chkShowNuyenChart": (
                "toggle_view",
                "chkShowNuyenChart_CheckedChanged",
                "2803637b7ca09a8b988e2593ce62a3bb9d5a6b1a01185d38d423d35203ff3321",
            ),
        }

        for control, (operation, handler, handler_digest) in expected.items():
            handlers = [{"event": "CheckedChanged", "handler": handler}]
            review = inventory._source_guarded_non_mutating_review(
                "CharacterCareer",
                control,
                handlers,
                [source],
            )
            self.assertIsNotNone(review)
            self.assertEqual(operation, review[0])
            self.assertIn("neither", review[1])
            self.assertEqual(handler_digest, inventory._legacy_method_digest([source], handler))

            identity = ("CharacterCareer", control)
            self.assertNotIn(identity, inventory.NON_MUTATING_LEGACY_INTERACTIONS)
            self.assertEqual(
                ("set_value", "definite"),
                inventory._operation(control, "direct_value_editor", handlers),
            )
            contract = inventory.SOURCE_GUARDED_NON_MUTATING_LEGACY_INTERACTIONS[identity]
            drifted_contract = {
                **contract,
                "methodDigests": {
                    **contract["methodDigests"],
                    handler: "0" * 64,
                },
            }
            with patch.dict(
                inventory.SOURCE_GUARDED_NON_MUTATING_LEGACY_INTERACTIONS,
                {identity: drifted_contract},
            ):
                self.assertIsNone(
                    inventory._source_guarded_non_mutating_review(
                        "CharacterCareer",
                        control,
                        handlers,
                        [source],
                    )
                )

        self.assertEqual(
            "0cd86a6f7d33dbbb4ffd7f504f187c04fc31914a94bac191025341b7912d55cf",
            inventory._legacy_method_digest([source], "RepopulateNuyenExpenseList"),
        )

        payload = json.loads(
            (REPO / "docs" / "ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json").read_text(
                encoding="utf-8"
            )
        )
        rows = {
            row["legacy"]["controlName"]: row
            for row in payload["rows"]
            if row["legacy"]["formOrControl"] == "CharacterCareer"
            and row["legacy"]["controlName"] in expected
        }
        self.assertEqual(set(expected), set(rows))
        for control, (operation, _, _) in expected.items():
            row = rows[control]
            self.assertEqual(operation, row["operation"])
            self.assertEqual("non_mutating", row["legacy"]["mutationDisposition"])
            self.assertFalse(row["editParityRequired"])
            self.assertEqual("not_applicable_non_mutating", row["phone"]["status"])
            self.assertEqual("not_applicable_non_mutating", row["tablet"]["status"])
            self.assertEqual("not_applicable_non_mutating", row["e2e"]["phone"]["status"])
            self.assertTrue(row["completionProven"])

    def test_create_contact_order_is_independently_source_guarded_non_mutating(self) -> None:
        legacy_root = (
            inventory.DEFAULT_CHUMMER5_ROOT
            if inventory.DEFAULT_CHUMMER5_ROOT.is_dir()
            else PRESENTATION_ROOT
        )
        source = (
            legacy_root
            / "Chummer"
            / "Forms"
            / "Character Forms"
            / "CharacterCreate.cs"
        ).read_bytes().decode("utf-8-sig")
        identity = ("CharacterCreate", "cmdSwapContactOrder")
        career_identity = ("CharacterCareer", "cmdSwapContactOrder")
        handler = "cmdSwapContactOrder_Click"
        handler_digest = "cf9933134fa9de0dbaea61a70982844b3eaec1a086f6765d20db521d32f35bf3"
        handlers = [{"event": "Click", "handler": handler}]

        review = inventory._source_guarded_non_mutating_review(
            identity[0],
            identity[1],
            handlers,
            [source],
        )
        self.assertIsNotNone(review)
        self.assertEqual("toggle_view", review[0])
        self.assertIn("panContacts.FlowDirection", review[1])
        self.assertIn("no character or contact model", review[1])
        self.assertEqual(handler_digest, inventory._legacy_method_digest([source], handler))
        self.assertIn(identity, inventory.SOURCE_GUARDED_NON_MUTATING_LEGACY_INTERACTIONS)
        self.assertIn(career_identity, inventory.SOURCE_GUARDED_NON_MUTATING_LEGACY_INTERACTIONS)
        self.assertIsNot(
            inventory.SOURCE_GUARDED_NON_MUTATING_LEGACY_INTERACTIONS[identity],
            inventory.SOURCE_GUARDED_NON_MUTATING_LEGACY_INTERACTIONS[career_identity],
        )
        self.assertNotIn(identity, inventory.NON_MUTATING_LEGACY_INTERACTIONS)
        self.assertEqual(
            ("mutating_action", "definite"),
            inventory._operation(identity[1], "action", handlers),
        )
        self.assertIsNone(
            inventory._source_guarded_non_mutating_review(
                identity[0],
                identity[1],
                [{"event": "Click", "handler": "drifted_handler"}],
                [source],
            )
        )

        contract = inventory.SOURCE_GUARDED_NON_MUTATING_LEGACY_INTERACTIONS[identity]
        drifted_contract = {
            **contract,
            "methodDigests": {handler: "0" * 64},
        }
        with patch.dict(
            inventory.SOURCE_GUARDED_NON_MUTATING_LEGACY_INTERACTIONS,
            {identity: drifted_contract},
        ):
            self.assertIsNone(
                inventory._source_guarded_non_mutating_review(
                    identity[0],
                    identity[1],
                    handlers,
                    [source],
                )
            )

        payload = json.loads(
            (REPO / "docs" / "ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json").read_text(
                encoding="utf-8"
            )
        )
        rows = [
            row
            for row in payload["rows"]
            if row["legacy"]["formOrControl"] == identity[0]
            and row["legacy"]["controlName"] == identity[1]
        ]
        self.assertEqual(1, len(rows))
        row = rows[0]
        self.assertEqual("toggle_view", row["operation"])
        self.assertEqual("non_mutating", row["legacy"]["mutationDisposition"])
        self.assertIn("panContacts.FlowDirection", row["legacy"]["dispositionEvidence"])
        self.assertFalse(row["editParityRequired"])
        self.assertEqual("not_applicable_non_mutating", row["phone"]["status"])
        self.assertEqual("not_applicable_non_mutating", row["tablet"]["status"])
        self.assertEqual("not_applicable_non_mutating", row["e2e"]["phone"]["status"])
        self.assertEqual("not_applicable_non_mutating", row["e2e"]["tablet"]["status"])
        self.assertTrue(row["completionProven"])

    def test_career_contact_order_is_source_guarded_non_mutating(self) -> None:
        legacy_root = (
            inventory.DEFAULT_CHUMMER5_ROOT
            if inventory.DEFAULT_CHUMMER5_ROOT.is_dir()
            else PRESENTATION_ROOT
        )
        source = (
            legacy_root
            / "Chummer"
            / "Forms"
            / "Character Forms"
            / "CharacterCareer.cs"
        ).read_bytes().decode("utf-8-sig")
        identity = ("CharacterCareer", "cmdSwapContactOrder")
        handler = "cmdSwapContactOrder_Click"
        handler_digest = "cf9933134fa9de0dbaea61a70982844b3eaec1a086f6765d20db521d32f35bf3"
        handlers = [{"event": "Click", "handler": handler}]

        review = inventory._source_guarded_non_mutating_review(
            identity[0],
            identity[1],
            handlers,
            [source],
        )
        self.assertIsNotNone(review)
        self.assertEqual("toggle_view", review[0])
        self.assertIn("panContacts.FlowDirection", review[1])
        self.assertIn("no character or contact model", review[1])
        self.assertEqual(handler_digest, inventory._legacy_method_digest([source], handler))
        self.assertNotIn(identity, inventory.NON_MUTATING_LEGACY_INTERACTIONS)
        self.assertEqual(
            ("mutating_action", "definite"),
            inventory._operation(identity[1], "action", handlers),
        )
        self.assertIsNone(
            inventory._source_guarded_non_mutating_review(
                identity[0],
                identity[1],
                [{"event": "Click", "handler": "drifted_handler"}],
                [source],
            )
        )

        contract = inventory.SOURCE_GUARDED_NON_MUTATING_LEGACY_INTERACTIONS[identity]
        drifted_contract = {
            **contract,
            "methodDigests": {handler: "0" * 64},
        }
        with patch.dict(
            inventory.SOURCE_GUARDED_NON_MUTATING_LEGACY_INTERACTIONS,
            {identity: drifted_contract},
        ):
            self.assertIsNone(
                inventory._source_guarded_non_mutating_review(
                    identity[0],
                    identity[1],
                    handlers,
                    [source],
                )
            )

        payload = json.loads(
            (REPO / "docs" / "ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json").read_text(
                encoding="utf-8"
            )
        )
        rows = [
            row
            for row in payload["rows"]
            if row["legacy"]["formOrControl"] == identity[0]
            and row["legacy"]["controlName"] == identity[1]
        ]
        self.assertEqual(1, len(rows))
        row = rows[0]
        self.assertEqual("toggle_view", row["operation"])
        self.assertEqual("non_mutating", row["legacy"]["mutationDisposition"])
        self.assertIn("panContacts.FlowDirection", row["legacy"]["dispositionEvidence"])
        self.assertFalse(row["editParityRequired"])
        self.assertEqual("not_applicable_non_mutating", row["phone"]["status"])
        self.assertEqual("not_applicable_non_mutating", row["tablet"]["status"])
        self.assertEqual("not_applicable_non_mutating", row["e2e"]["phone"]["status"])
        self.assertEqual("not_applicable_non_mutating", row["e2e"]["tablet"]["status"])
        self.assertTrue(row["completionProven"])

    def test_career_contacts_expansion_is_source_and_callee_guarded_non_mutating(self) -> None:
        legacy_root = (
            inventory.DEFAULT_CHUMMER5_ROOT
            if inventory.DEFAULT_CHUMMER5_ROOT.is_dir()
            else PRESENTATION_ROOT
        )
        career_source = (
            legacy_root
            / "Chummer"
            / "Forms"
            / "Character Forms"
            / "CharacterCareer.cs"
        ).read_bytes().decode("utf-8-sig")
        contact_source = (
            legacy_root
            / "Chummer"
            / "Controls"
            / "Characters"
            / "ContactControl.cs"
        ).read_bytes().decode("utf-8-sig")
        identity = ("CharacterCareer", "cmdContactsExpansionToggle")
        handler = "cmdContactsExpansionToggle_Click"
        handlers = [{"event": "Click", "handler": handler}]
        all_class_texts = {
            ("Chummer", "CharacterCareer"): [career_source],
            ("Chummer", "ContactControl"): [contact_source],
        }
        expected_digests = {
            handler: "86f560865dcc9355d6ae281241b6d6b3a383467225bab689ff3e47745a239e0f",
            "GetExpandedAsync": "86c89244744903cb9fcc0393f5af724c44cb54a1d22913bd20739a1fc0813652",
            "SetExpandedAsync": "d8e7b92d31a5c59167221da51e7ca51b2da351c845662964d02644c919d4cb35",
            "CreateSecondRowAsync": "3d6fd6d0e0092da8f86b3c305a9883eb1d958e20848a226f823bc211929335b5",
            "CreateStatBlockAsync": "9ee96c36c405eece525890c18b46299395d477a04bffc278db356cbce0e5b7f6",
            "LoadStatBlockListsAsync": "8d1eec5bf6905b96a722c3f9a195cedcd4fc1b3697438ca2f2f1ea7f808245ac",
        }

        review = inventory._source_guarded_non_mutating_review(
            identity[0],
            identity[1],
            handlers,
            [career_source],
            all_class_texts,
        )
        self.assertIsNotNone(review)
        self.assertEqual("toggle_view", review[0])
        self.assertIn("transient expanded state", review[1])
        self.assertIn("writes neither runner XML", review[1])
        self.assertEqual(
            expected_digests[handler],
            inventory._legacy_method_digest([career_source], handler),
        )
        for method_name in expected_digests.keys() - {handler}:
            self.assertEqual(
                expected_digests[method_name],
                inventory._legacy_method_digest([contact_source], method_name),
            )
        self.assertNotIn(identity, inventory.NON_MUTATING_LEGACY_INTERACTIONS)
        self.assertEqual(
            ("toggle", "definite"),
            inventory._operation(identity[1], "action", handlers),
        )
        self.assertIsNone(
            inventory._source_guarded_non_mutating_review(
                identity[0],
                identity[1],
                [{"event": "Click", "handler": "drifted_handler"}],
                [career_source],
                all_class_texts,
            )
        )
        self.assertIsNone(
            inventory._source_guarded_non_mutating_review(
                identity[0],
                identity[1],
                handlers,
                [career_source],
            )
        )

        contract = inventory.SOURCE_GUARDED_NON_MUTATING_LEGACY_INTERACTIONS[identity]
        with patch.dict(
            inventory.SOURCE_GUARDED_NON_MUTATING_LEGACY_INTERACTIONS,
            {
                identity: {
                    **contract,
                    "methodDigests": {handler: "0" * 64},
                },
            },
        ):
            self.assertIsNone(
                inventory._source_guarded_non_mutating_review(
                    identity[0],
                    identity[1],
                    handlers,
                    [career_source],
                    all_class_texts,
                )
            )

        callee_contracts = contract["calleeMethodDigests"]
        for callee_name in expected_digests.keys() - {handler}:
            drifted_callees = {
                callee_identity: dict(method_digests)
                for callee_identity, method_digests in callee_contracts.items()
            }
            drifted_callees[("Chummer", "ContactControl")][callee_name] = "0" * 64
            with patch.dict(
                inventory.SOURCE_GUARDED_NON_MUTATING_LEGACY_INTERACTIONS,
                {
                    identity: {
                        **contract,
                        "calleeMethodDigests": drifted_callees,
                    },
                },
            ):
                self.assertIsNone(
                    inventory._source_guarded_non_mutating_review(
                        identity[0],
                        identity[1],
                        handlers,
                        [career_source],
                        all_class_texts,
                    )
                )

        payload = json.loads(
            (REPO / "docs" / "ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json").read_text(
                encoding="utf-8"
            )
        )
        rows = [
            row
            for row in payload["rows"]
            if row["legacy"]["formOrControl"] == identity[0]
            and row["legacy"]["controlName"] == identity[1]
        ]
        self.assertEqual(1, len(rows))
        row = rows[0]
        self.assertEqual("toggle_view", row["operation"])
        self.assertEqual("non_mutating", row["legacy"]["mutationDisposition"])
        self.assertIn("transient expanded state", row["legacy"]["dispositionEvidence"])
        self.assertFalse(row["editParityRequired"])
        self.assertEqual("not_applicable_non_mutating", row["phone"]["status"])
        self.assertEqual("not_applicable_non_mutating", row["tablet"]["status"])
        self.assertEqual("not_applicable_non_mutating", row["e2e"]["phone"]["status"])
        self.assertEqual("not_applicable_non_mutating", row["e2e"]["tablet"]["status"])
        self.assertTrue(row["completionProven"])

    def test_career_attribute_category_is_reviewed_as_transient_shapeshifter_view(self) -> None:
        payload = json.loads(
            (REPO / "docs" / "ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json").read_text(
                encoding="utf-8"
            )
        )
        rows = [
            row
            for row in payload["rows"]
            if row["legacy"]["formOrControl"] == "CharacterCareer"
            and row["legacy"]["controlName"] == "cboAttributeCategory"
        ]

        self.assertEqual(1, len(rows))
        row = rows[0]
        self.assertEqual("select_shapeshifter_attribute_view", row["operation"])
        self.assertEqual("non_mutating", row["legacy"]["mutationDisposition"])
        self.assertIn(
            "cboAttributeCategory_SelectedIndexChanged",
            row["legacy"]["dispositionEvidence"],
        )
        self.assertIn("SetAttributeCategoryAsync", row["legacy"]["dispositionEvidence"])
        self.assertIn("current in-memory Print projection", row["legacy"]["dispositionEvidence"])
        self.assertIn("always resets the selection to Standard", row["legacy"]["dispositionEvidence"])
        self.assertIn("Save only serializes CharacterAttrib values", row["legacy"]["dispositionEvidence"])
        self.assertIn("Load never restores _eAttributeCategory", row["legacy"]["dispositionEvidence"])
        self.assertFalse(row["editParityRequired"])
        self.assertEqual("not_applicable_non_mutating", row["phone"]["status"])
        self.assertEqual("not_applicable_non_mutating", row["tablet"]["status"])
        self.assertEqual("not_applicable_non_mutating", row["e2e"]["phone"]["status"])
        self.assertEqual("not_applicable_non_mutating", row["e2e"]["tablet"]["status"])
        self.assertTrue(row["completionProven"])

    def test_included_in_armor_checkboxes_are_reviewed_read_only_displays(self) -> None:
        payload = json.loads(
            (REPO / "docs" / "ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json").read_text(
                encoding="utf-8"
            )
        )
        rows = [
            row
            for row in payload["rows"]
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] == "chkIncludedInArmor"
        ]

        self.assertEqual(2, len(rows))
        for row in rows:
            self.assertEqual("display_included_in_armor_state", row["operation"])
            self.assertEqual("non_mutating", row["legacy"]["mutationDisposition"])
            self.assertIn("disabled in the designer", row["legacy"]["dispositionEvidence"])
            self.assertIn("no mutation event", row["legacy"]["dispositionEvidence"])
            self.assertFalse(row["editParityRequired"])
            self.assertEqual("not_applicable_non_mutating", row["phone"]["status"])
            self.assertEqual("not_applicable_non_mutating", row["tablet"]["status"])
            self.assertTrue(row["completionProven"])

    def test_vehicle_included_and_gear_filter_checkboxes_are_reviewed_non_mutating(self) -> None:
        payload = json.loads(
            (REPO / "docs" / "ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json").read_text(
                encoding="utf-8"
            )
        )
        expected = {
            ("CharacterCreate", "chkVehicleIncludedInWeapon"): "display_included_in_weapon_state",
            ("CharacterCareer", "chkVehicleIncludedInWeapon"): "display_included_in_weapon_state",
            ("CharacterCreate", "chkCommlinks"): "filter_gear_view",
            ("CharacterCareer", "chkCommlinks"): "filter_gear_view",
            ("CharacterCareer", "chkHideLoadedAmmo"): "filter_gear_view",
        }
        rows = {
            (row["legacy"]["formOrControl"], row["legacy"]["controlName"]): row
            for row in payload["rows"]
            if (row["legacy"]["formOrControl"], row["legacy"]["controlName"]) in expected
        }

        self.assertEqual(set(expected), set(rows))
        for identity, operation in expected.items():
            row = rows[identity]
            self.assertEqual(operation, row["operation"])
            self.assertEqual("non_mutating", row["legacy"]["mutationDisposition"])
            self.assertIn("writes no runner", row["legacy"]["dispositionEvidence"])
            self.assertFalse(row["editParityRequired"])
            self.assertEqual("not_applicable_non_mutating", row["phone"]["status"])
            self.assertEqual("not_applicable_non_mutating", row["tablet"]["status"])
            self.assertTrue(row["completionProven"])

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
            payload["summary"]["reviewedNonMutatingCount"] + 75,
            payload["summary"]["completionProvenCount"],
        )
        self.assertEqual(len(rows), len({row["id"] for row in rows}))

        expected_note_controls = {
            ("CharacterCreate", "rtfNotes"): "character-notes-editor",
            ("CharacterCreate", "txtGroupNotes"): "character-group-notes-editor",
            ("CharacterCareer", "rtfNotes"): "character-notes-editor",
            ("CharacterCareer", "rtfGameNotes"): "character-game-notes-editor",
            ("CharacterCareer", "txtGroupNotes"): "character-group-notes-editor",
        }
        character_notes = [
            row for row in rows
            if (
                row["legacy"]["formOrControl"],
                row["legacy"]["controlName"],
            ) in expected_note_controls
        ]
        self.assertEqual(5, len(character_notes))
        for row in character_notes:
            key = (
                row["legacy"]["formOrControl"],
                row["legacy"]["controlName"],
            )
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual("Build > Notes", row["phone"]["route"])
            self.assertEqual(expected_note_controls[key], row["phone"]["automationId"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertIn(
                row["e2e"]["phone"]["status"],
                {"scripted_not_executed", "executed_api36"},
            )
            self.assertTrue(row["e2e"]["phone"]["ref"])

        career_reputation = [
            row for row in rows
            if row["legacy"]["formOrControl"] == "CharacterCareer"
            and row["legacy"]["controlName"] in inventory.CAREER_REPUTATION_CONTROLS
        ]
        self.assertEqual(5, len(career_reputation))
        for row in career_reputation:
            xml_element, automation_id, _property = inventory.CAREER_REPUTATION_CONTROLS[
                row["legacy"]["controlName"]
            ]
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual("Build > Reputation", row["phone"]["route"])
            self.assertEqual("CareerReputationPage", row["phone"]["surface"])
            self.assertEqual(automation_id, row["phone"]["automationId"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_career_reputation_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertIn(xml_element, row["persistenceAssertion"])
            self.assertFalse(row["completionProven"])

        burn_street_cred = next(
            row for row in rows
            if row["legacy"]["formOrControl"] == "CharacterCareer"
            and row["legacy"]["controlName"] == inventory.BURN_STREET_CRED_CONTROL
        )
        self.assertEqual("implemented_pending_emulator", burn_street_cred["phone"]["status"])
        self.assertEqual(
            "Build > Reputation > Burn 2 Street Cred",
            burn_street_cred["phone"]["route"],
        )
        self.assertEqual("CareerReputationPage", burn_street_cred["phone"]["surface"])
        self.assertEqual(
            "career-reputation-burn-street-cred",
            burn_street_cred["phone"]["automationId"],
        )
        self.assertEqual("missing", burn_street_cred["tablet"]["status"])
        self.assertEqual("scripted_not_executed", burn_street_cred["e2e"]["phone"]["status"])
        self.assertEqual(
            "tests/run_api36_career_reputation_e2e.py",
            burn_street_cred["e2e"]["phone"]["ref"],
        )
        self.assertIn("burntstreetcred", burn_street_cred["persistenceAssertion"])
        self.assertFalse(burn_street_cred["completionProven"])

        situational_modifiers = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] in inventory.SITUATIONAL_MODIFIER_CONTROLS
        ]
        self.assertEqual(4, len(situational_modifiers))
        for row in situational_modifiers:
            xml_element, automation_id, _property = inventory.SITUATIONAL_MODIFIER_CONTROLS[
                row["legacy"]["controlName"]
            ]
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual("Build > Situational modifiers", row["phone"]["route"])
            self.assertEqual("SituationalModifiersPage", row["phone"]["surface"])
            self.assertEqual(automation_id, row["phone"]["automationId"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_situational_modifiers_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertIn(xml_element, row["persistenceAssertion"])
            self.assertFalse(row["completionProven"])

        primary_arm = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] in inventory.PRIMARY_ARM_CONTROLS
        ]
        self.assertEqual(2, len(primary_arm))
        for row in primary_arm:
            xml_element, automation_id, _property = inventory.PRIMARY_ARM_CONTROLS[
                row["legacy"]["controlName"]
            ]
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual("Build > Primary arm", row["phone"]["route"])
            self.assertEqual("PrimaryArmPage", row["phone"]["surface"])
            self.assertEqual(automation_id, row["phone"]["automationId"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_primary_arm_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertIn(xml_element, row["persistenceAssertion"])
            self.assertFalse(row["completionProven"])

        group_membership = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] == inventory.GROUP_MEMBERSHIP_CONTROL
        ]
        self.assertEqual(2, len(group_membership))
        for row in group_membership:
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual("Build > Runner > Group membership", row["phone"]["route"])
            self.assertEqual("GroupMembershipPage", row["phone"]["surface"])
            self.assertEqual("group-membership-toggle", row["phone"]["automationId"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual("tests/run_api36_group_membership_e2e.py", row["e2e"]["phone"]["ref"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertIn("groupmember", row["persistenceAssertion"])
            self.assertIn("KarmaJoinGroup", row["phone"]["coverageLimit"])
            self.assertFalse(row["completionProven"])

        group_name = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] == inventory.GROUP_NAME_CONTROL
        ]
        self.assertEqual(2, len(group_name))
        for row in group_name:
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual("Build > Runner > Group name", row["phone"]["route"])
            self.assertEqual("GroupNamePage", row["phone"]["surface"])
            self.assertEqual("group-name-value", row["phone"]["automationId"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual("tests/run_api36_group_name_e2e.py", row["e2e"]["phone"]["ref"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertIn("groupname", row["persistenceAssertion"])
            self.assertIn("SelectContactConnection.txtGroupName", row["phone"]["coverageLimit"])
            self.assertFalse(row["completionProven"])

        contact_group_name = [
            row for row in rows
            if row["legacy"]["formOrControl"] == "SelectContactConnection"
            and row["legacy"]["controlName"] == inventory.GROUP_NAME_CONTROL
        ]
        self.assertEqual(1, len(contact_group_name))
        self.assertEqual("missing", contact_group_name[0]["phone"]["status"])

        tradition_name = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] == inventory.TRADITION_NAME_CONTROL
        ]
        self.assertEqual(2, len(tradition_name))
        for row in tradition_name:
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual("Build > Magic > Custom tradition name", row["phone"]["route"])
            self.assertEqual("TraditionNamePage", row["phone"]["surface"])
            self.assertEqual("tradition-name-value", row["phone"]["automationId"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual("tests/run_api36_tradition_name_e2e.py", row["e2e"]["phone"]["ref"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertIn("tradition/name", row["persistenceAssertion"])
            self.assertIn("616ba093-306c-45fc-8f41-0b98c8cccb46", row["phone"]["coverageLimit"])
            self.assertFalse(row["completionProven"])

        tradition_drain = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] == inventory.TRADITION_DRAIN_CONTROL
        ]
        self.assertEqual(2, len(tradition_drain))
        for row in tradition_drain:
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual("Build > Magic > Tradition drain", row["phone"]["route"])
            self.assertEqual("TraditionDrainPage", row["phone"]["surface"])
            self.assertEqual("tradition-drain-value", row["phone"]["automationId"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual("tests/run_api36_tradition_drain_e2e.py", row["e2e"]["phone"]["ref"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertIn("tradition/drain", row["persistenceAssertion"])
            self.assertIn("traditions.xml/drainattributes", row["phone"]["coverageLimit"])
            self.assertFalse(row["completionProven"])

        tradition_spirit_categories = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] in inventory.TRADITION_SPIRIT_CATEGORY_CONTROLS
        ]
        self.assertEqual(10, len(tradition_spirit_categories))
        self.assertEqual(
            {"CharacterCreate", "CharacterCareer"},
            {row["legacy"]["formOrControl"] for row in tradition_spirit_categories},
        )
        for row in tradition_spirit_categories:
            _, xml_element, token = inventory.TRADITION_SPIRIT_CATEGORY_CONTROLS[
                row["legacy"]["controlName"]
            ]
            self.assertEqual("spirits_and_sprites", row["mutationFamily"])
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual("Build > Magic > Tradition spirits", row["phone"]["route"])
            self.assertEqual("TraditionSpiritCategoryPage", row["phone"]["surface"])
            self.assertEqual(
                f"tradition-spirit-{token}-value",
                row["phone"]["automationId"],
            )
            self.assertIn("all five field-local revisions", row["presenterMutation"])
            self.assertIn(f"tradition/{xml_element}", row["persistenceAssertion"])
            self.assertIn("LimitSpiritCategory", row["persistenceAssertion"])
            self.assertIn("atomic save", row["persistenceAssertion"])
            self.assertIn("custom-data overlays", row["phone"]["coverageLimit"])
            self.assertIn("Non-Custom", row["phone"]["coverageLimit"])
            self.assertIn("RES", row["phone"]["coverageLimit"])
            self.assertIn("catalog revision drift fail closed", row["phone"]["coverageLimit"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_tradition_spirit_categories_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertFalse(row["completionProven"])

        gear_name = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] == inventory.GEAR_NAME_CONTROL
        ]
        self.assertEqual(2, len(gear_name))
        for row in gear_name:
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual("Build > Gear > selected stable Gear > Gear Name", row["phone"]["route"])
            self.assertEqual("CollectionItemEditorPage", row["phone"]["surface"])
            self.assertEqual("collection-field-gearname-{stable-gear-guid}", row["phone"]["automationId"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual("tests/run_api36_gear_name_e2e.py", row["e2e"]["phone"]["ref"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertIn("gear/gearname", row["persistenceAssertion"])
            self.assertIn("32767-character", row["phone"]["coverageLimit"])
            self.assertFalse(row["completionProven"])

        lifestyle_name = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] == inventory.LIFESTYLE_NAME_CONTROL
        ]
        self.assertEqual(2, len(lifestyle_name))
        for row in lifestyle_name:
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual(
                "Build > Lifestyle > Lifestyles > selected stable Lifestyle > Lifestyle Name",
                row["phone"]["route"],
            )
            self.assertEqual("CollectionItemEditorPage", row["phone"]["surface"])
            self.assertEqual(
                "collection-field-lifestylename-{stable-lifestyle-guid}",
                row["phone"]["automationId"],
            )
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual("tests/run_api36_lifestyle_name_e2e.py", row["e2e"]["phone"]["ref"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertIn("lifestyles/lifestyle/extra", row["persistenceAssertion"])
            self.assertIn("32767-character", row["phone"]["coverageLimit"])
            self.assertIn("notesColor", row["persistenceAssertion"])
            self.assertFalse(row["completionProven"])

        lifestyle_notes = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] in inventory.LIFESTYLE_NOTES_CONTROLS
        ]
        self.assertEqual(4, len(lifestyle_notes))
        for row in lifestyle_notes:
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual(
                "Build > Lifestyle > Lifestyles > selected stable Lifestyle > Notes + Notes Color",
                row["phone"]["route"],
            )
            self.assertEqual("CollectionItemEditorPage", row["phone"]["surface"])
            self.assertEqual(
                "collection-field-notes-{stable-lifestyle-guid} + "
                "collection-field-notescolor-{stable-lifestyle-guid}",
                row["phone"]["automationId"],
            )
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual("tests/run_api36_lifestyle_name_e2e.py", row["e2e"]["phone"]["ref"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertIn("notes and canonical notesColor together", row["persistenceAssertion"])
            self.assertIn("effectively-unbounded RichTextBox", row["phone"]["coverageLimit"])
            self.assertIn("patched together", row["phone"]["coverageLimit"])
            self.assertFalse(row["completionProven"])

        lifestyle_increments = [
            row for row in rows
            if (row["legacy"]["formOrControl"], row["legacy"]["controlName"])
            in inventory.LIFESTYLE_INCREMENT_CONTROLS
        ]
        self.assertEqual(3, len(lifestyle_increments))
        expected_actions = {
            (form, control): action
            for (form, control), (action, _) in inventory.LIFESTYLE_INCREMENT_CONTROLS.items()
        }
        for row in lifestyle_increments:
            identity = (row["legacy"]["formOrControl"], row["legacy"]["controlName"])
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual(
                "Build > Lifestyle > Lifestyles > selected stable Lifestyle > Lifestyle Intervals",
                row["phone"]["route"],
            )
            self.assertEqual("LifestyleIncrementPage", row["phone"]["surface"])
            self.assertIn(expected_actions[identity], row["presenterMutation"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual("tests/run_api36_lifestyle_increments_e2e.py", row["e2e"]["phone"]["ref"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertIn("derived totalcost", row["persistenceAssertion"])
            self.assertIn("lack of a lower bound", row["phone"]["coverageLimit"])
            self.assertFalse(row["completionProven"])

        gear_locations = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] == inventory.GEAR_LOCATION_ADD_CONTROL
        ]
        self.assertEqual(2, len(gear_locations))
        for row in gear_locations:
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual("Build > Gear > Gear Locations > Add gear location", row["phone"]["route"])
            self.assertEqual("GearLocationAddPage", row["phone"]["surface"])
            self.assertEqual("gear-location-add", row["phone"]["automationId"])
            self.assertEqual(
                "ICharacterOverviewPresenter.ApplyGearLocationAddAsync(GearLocationAddRequest)",
                row["presenterMutation"],
            )
            self.assertIn("stable-guid", row["persistenceAssertion"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_gear_location_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertFalse(row["completionProven"])

        weapon_locations = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] == inventory.WEAPON_LOCATION_ADD_CONTROL
        ]
        self.assertEqual(2, len(weapon_locations))
        for row in weapon_locations:
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual(
                "Build > Gear > Weapon Locations > Add weapon location",
                row["phone"]["route"],
            )
            self.assertEqual("WeaponLocationAddPage", row["phone"]["surface"])
            self.assertEqual("weapon-location-add", row["phone"]["automationId"])
            self.assertEqual(
                "ICharacterOverviewPresenter.ApplyWeaponLocationAddAsync(WeaponLocationAddRequest)",
                row["presenterMutation"],
            )
            self.assertIn("stable-guid", row["persistenceAssertion"])
            self.assertIn("existing locations remain unchanged", row["persistenceAssertion"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_weapon_location_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertFalse(row["completionProven"])

        vehicle_locations = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] == inventory.VEHICLE_LOCATION_ADD_CONTROL
        ]
        self.assertEqual(2, len(vehicle_locations))
        for row in vehicle_locations:
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual(
                "Build > Gear > Vehicle Locations > Add vehicle location OR "
                "Build > Gear > Vehicles > selected stable vehicle > Add location to vehicle",
                row["phone"]["route"],
            )
            self.assertEqual("VehicleLocationAddPage", row["phone"]["surface"])
            self.assertEqual(
                "vehicle-location-add-{global|stable-vehicle-guid}",
                row["phone"]["automationId"],
            )
            self.assertIn("null for global", row["presenterMutation"])
            self.assertIn("stable vehicle Guid", row["presenterMutation"])
            self.assertIn("character/vehiclelocations/location", row["persistenceAssertion"])
            self.assertIn("character/vehicles/vehicle[stable Guid]/locations/location", row["persistenceAssertion"])
            self.assertIn("untouched-vehicle", row["persistenceAssertion"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_vehicle_location_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertFalse(row["completionProven"])

        vehicle_home_nodes = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] == inventory.VEHICLE_HOME_NODE_CONTROL
        ]
        self.assertEqual(2, len(vehicle_home_nodes))
        for row in vehicle_home_nodes:
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual(
                "Build > Gear > Vehicles > selected stable vehicle > Vehicle Home Node",
                row["phone"]["route"],
            )
            self.assertEqual("VehicleHomeNodePage", row["phone"]["surface"])
            self.assertEqual(
                "vehicle-home-node-toggle-{stable-vehicle-guid}",
                row["phone"]["automationId"],
            )
            self.assertIn("expected content revision", row["presenterMutation"])
            self.assertIn("every other saved homenode False", row["persistenceAssertion"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_vehicle_home_node_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertFalse(row["completionProven"])

        armor_home_nodes = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] == inventory.ARMOR_HOME_NODE_CONTROL
        ]
        self.assertEqual(2, len(armor_home_nodes))
        for row in armor_home_nodes:
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual(
                "Build > Gear > Armor > selected stable armor > Armor Home Node",
                row["phone"]["route"],
            )
            self.assertEqual("ArmorHomeNodePage", row["phone"]["surface"])
            self.assertEqual(
                "armor-home-node-toggle-{stable-armor-guid}",
                row["phone"]["automationId"],
            )
            self.assertIn("expected content revision", row["presenterMutation"])
            self.assertIn("every other saved homenode False", row["persistenceAssertion"])
            self.assertIn("atomic save", row["persistenceAssertion"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_armor_home_node_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertFalse(row["completionProven"])

        weapon_home_nodes = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] == inventory.WEAPON_HOME_NODE_CONTROL
        ]
        self.assertEqual(2, len(weapon_home_nodes))
        for row in weapon_home_nodes:
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual(
                "Build > Gear > Weapons > selected stable AI weapon > Weapon Home Node",
                row["phone"]["route"],
            )
            self.assertEqual("WeaponHomeNodePage", row["phone"]["surface"])
            self.assertEqual(
                "weapon-home-node-toggle-{stable-weapon-guid}",
                row["phone"]["automationId"],
            )
            self.assertIn("exact AI/Device Rating/Program Limit/DEP", row["presenterMutation"])
            self.assertIn("DEP > Device Rating ? 2 : 1", row["persistenceAssertion"])
            self.assertIn("every other recognized saved homenode False", row["persistenceAssertion"])
            self.assertIn("atomic save", row["persistenceAssertion"])
            self.assertIn("fail closed", row["phone"]["coverageLimit"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_weapon_home_node_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertFalse(row["completionProven"])

        weapon_active_commlinks = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] == inventory.WEAPON_ACTIVE_COMMLINK_CONTROL
        ]
        self.assertEqual(2, len(weapon_active_commlinks))
        for row in weapon_active_commlinks:
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual(
                "Build > Gear > Weapons > selected stable commlink weapon > Weapon Active Commlink",
                row["phone"]["route"],
            )
            self.assertEqual("WeaponActiveCommlinkPage", row["phone"]["surface"])
            self.assertEqual(
                "weapon-active-commlink-toggle-{stable-weapon-guid}",
                row["phone"]["automationId"],
            )
            self.assertIn("exact persona eligibility", row["presenterMutation"])
            self.assertIn("every other recognized saved matrix-device active False", row["persistenceAssertion"])
            self.assertIn("unrelated active XML", row["persistenceAssertion"])
            self.assertIn("atomic save", row["persistenceAssertion"])
            self.assertIn("WeaponAccessory/Gear tree selections", row["phone"]["coverageLimit"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_weapon_active_commlink_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertFalse(row["completionProven"])

        armor_active_commlinks = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] == inventory.ARMOR_ACTIVE_COMMLINK_CONTROL
        ]
        self.assertEqual(2, len(armor_active_commlinks))
        for row in armor_active_commlinks:
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual(
                "Build > Gear > Armor > selected stable persona-capable armor > Armor Active Commlink",
                row["phone"]["route"],
            )
            self.assertEqual("ArmorActiveCommlinkPage", row["phone"]["surface"])
            self.assertEqual(
                "armor-active-commlink-toggle-{stable-armor-guid}",
                row["phone"]["automationId"],
            )
            self.assertIn("exact persona eligibility", row["presenterMutation"])
            self.assertIn("every other saved matrix-device active False", row["persistenceAssertion"])
            self.assertIn("unrelated active XML", row["persistenceAssertion"])
            self.assertIn("atomic save", row["persistenceAssertion"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_armor_active_commlink_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertFalse(row["completionProven"])

        gear_active_commlinks = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] == inventory.GEAR_ACTIVE_COMMLINK_CONTROL
        ]
        self.assertEqual(2, len(gear_active_commlinks))
        for row in gear_active_commlinks:
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual(
                "Build > Gear > selected stable persona-capable gear > Gear Active Commlink",
                row["phone"]["route"],
            )
            self.assertEqual("GearActiveCommlinkPage", row["phone"]["surface"])
            self.assertEqual(
                "gear-active-commlink-toggle-{stable-gear-guid}",
                row["phone"]["automationId"],
            )
            self.assertIn("full expected Core semantics", row["presenterMutation"])
            self.assertIn("every other recognized saved matrix-device active False", row["persistenceAssertion"])
            self.assertIn("unrelated active XML", row["persistenceAssertion"])
            self.assertIn("revision-checked atomic save", row["persistenceAssertion"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_gear_active_commlink_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertFalse(row["completionProven"])

        prototype_transhuman_rows = [
            row for row in rows
            if row["legacy"]["formOrControl"] == "CharacterCreate"
            and row["legacy"]["controlName"] == inventory.PROTOTYPE_TRANSHUMAN_CONTROL
        ]
        self.assertEqual(1, len(prototype_transhuman_rows))
        prototype_transhuman = prototype_transhuman_rows[0]
        self.assertEqual("CharacterCreate", prototype_transhuman["legacy"]["formOrControl"])
        self.assertEqual("cyberware", prototype_transhuman["mutationFamily"])
        self.assertEqual("implemented_pending_emulator", prototype_transhuman["phone"]["status"])
        self.assertEqual(
            "Build > Gear > Cyberware > selected stable top-level Bioware > Prototype Transhuman",
            prototype_transhuman["phone"]["route"],
        )
        self.assertEqual("PrototypeTranshumanPage", prototype_transhuman["phone"]["surface"])
        self.assertEqual(
            "prototype-transhuman-toggle-{stable-cyberware-guid}",
            prototype_transhuman["phone"]["automationId"],
        )
        self.assertIn("full expected Core hierarchy/allowance semantics", prototype_transhuman["presenterMutation"])
        self.assertIn("exact stable recursive hierarchy", prototype_transhuman["persistenceAssertion"])
        self.assertIn("unrelated cyberware", prototype_transhuman["persistenceAssertion"])
        self.assertIn("revision-checked atomic save", prototype_transhuman["persistenceAssertion"])
        self.assertIn("Career has no authoritative checkbox row", prototype_transhuman["phone"]["coverageLimit"])
        self.assertEqual("missing", prototype_transhuman["tablet"]["status"])
        self.assertEqual("scripted_not_executed", prototype_transhuman["e2e"]["phone"]["status"])
        self.assertEqual(
            "tests/run_api36_prototype_transhuman_e2e.py",
            prototype_transhuman["e2e"]["phone"]["ref"],
        )
        self.assertFalse(prototype_transhuman["completionProven"])
        self.assertFalse(any(
            row["legacy"]["formOrControl"] == "CharacterCareer"
            and row["legacy"]["controlName"] == inventory.PROTOTYPE_TRANSHUMAN_CONTROL
            for row in rows
        ))

        quality_levels = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] == inventory.QUALITY_LEVEL_CONTROL
        ]
        self.assertEqual(2, len(quality_levels))
        for row in quality_levels:
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual(
                "Build > Qualities > selected stable exact Quality > Quality Level",
                row["phone"]["route"],
            )
            self.assertEqual("QualityLevelPage", row["phone"]["surface"])
            self.assertEqual(
                "quality-level-value-{stable-quality-guid}",
                row["phone"]["automationId"],
            )
            self.assertIn("SourceID+Extra+SourceName+Type", row["presenterMutation"])
            self.assertIn("expected content revision", row["presenterMutation"])
            self.assertIn("fresh-Guid duplicate saved qualities", row["persistenceAssertion"])
            self.assertIn("zero-Karma AddQuality or negative RemoveQuality undo expenses", row["persistenceAssertion"])
            self.assertIn("atomic save", row["persistenceAssertion"])
            self.assertIn("saved BP 0", row["phone"]["coverageLimit"])
            self.assertIn("requirements", row["phone"]["coverageLimit"])
            self.assertIn("paid Career levels", row["phone"]["coverageLimit"])
            self.assertIn("fail closed", row["phone"]["coverageLimit"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_quality_level_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertFalse(row["completionProven"])

        armor_damage = [
            row for row in rows
            if row["legacy"]["formOrControl"] == "CharacterCareer"
            and row["legacy"]["controlName"] in inventory.ARMOR_DAMAGE_CONTROLS
        ]
        self.assertEqual(2, len(armor_damage))
        self.assertFalse(any(
            row["legacy"]["formOrControl"] == "CharacterCreate"
            and row["legacy"]["controlName"] in inventory.ARMOR_DAMAGE_CONTROLS
            for row in rows
        ))
        for row in armor_damage:
            _, action, automation_id = inventory.ARMOR_DAMAGE_CONTROLS[
                row["legacy"]["controlName"]
            ]
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual(
                "Build > Gear > Armor > selected stable Career armor > Armor Condition",
                row["phone"]["route"],
            )
            self.assertEqual("ArmorDamagePage", row["phone"]["surface"])
            self.assertEqual(automation_id, row["phone"]["automationId"])
            self.assertIn(f"CharacterArmorDamageAdjustment.{action.title()}", row["presenterMutation"])
            self.assertIn("stable top-level armor Guid", row["presenterMutation"])
            self.assertIn("primary/override half-armor bound", row["persistenceAssertion"])
            self.assertIn("atomic save", row["persistenceAssertion"])
            self.assertIn("ArmorDegradation", row["phone"]["coverageLimit"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_armor_damage_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertFalse(row["completionProven"])

        armor_equipment = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] in inventory.ARMOR_EQUIPMENT_CONTROLS
        ]
        self.assertEqual(6, len(armor_equipment))
        for row in armor_equipment:
            _, action, automation_id = inventory.ARMOR_EQUIPMENT_CONTROLS[
                row["legacy"]["controlName"]
            ]
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual(
                "Build > Gear > Armor > selected stable armor > Armor Equipment",
                row["phone"]["route"],
            )
            self.assertEqual("ArmorEquipmentPage", row["phone"]["surface"])
            self.assertEqual(automation_id, row["phone"]["automationId"])
            self.assertIn(action, row["presenterMutation"])
            self.assertIn("stable top-level armor Guid", row["presenterMutation"])
            self.assertIn("nested armor-mod/gear equipped flags", row["persistenceAssertion"])
            self.assertIn("atomic save", row["persistenceAssertion"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual("tests/run_api36_armor_equipment_e2e.py", row["e2e"]["phone"]["ref"])
            self.assertFalse(row["completionProven"])

        armor_tree_flags = [
            row for row in rows
            if row["legacy"]["formOrControl"] == "CharacterCreate"
            and row["legacy"]["controlName"] in inventory.ARMOR_TREE_FLAG_CONTROLS
        ]
        self.assertEqual(2, len(armor_tree_flags))
        for row in armor_tree_flags:
            _, xml_element, automation_id = inventory.ARMOR_TREE_FLAG_CONTROLS[
                row["legacy"]["controlName"]
            ]
            self.assertEqual("armor", row["mutationFamily"])
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual(
                "Build > Gear > Armor > selected stable Armor > Armor tree flags",
                row["phone"]["route"],
            )
            self.assertEqual("ArmorTreeFlagPage", row["phone"]["surface"])
            self.assertEqual(automation_id, row["phone"]["automationId"])
            self.assertIn("typed Armor/ArmorMod/recursive Gear hierarchy", row["presenterMutation"])
            self.assertIn(f".../{xml_element}", row["persistenceAssertion"])
            self.assertIn("under Armor/ArmorMod", row["persistenceAssertion"])
            self.assertIn("revision-bound atomic save", row["persistenceAssertion"])
            self.assertIn("no source/cost eligibility is invented", row["phone"]["coverageLimit"])
            self.assertIn("CharacterCareer", row["phone"]["coverageLimit"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_armor_tree_flags_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertFalse(row["completionProven"])

        gear_stolen = [
            row for row in rows
            if row["legacy"]["formOrControl"] == "CharacterCreate"
            and row["legacy"]["controlName"] == inventory.GEAR_STOLEN_CONTROL
        ]
        self.assertEqual(1, len(gear_stolen))
        row = gear_stolen[0]
        self.assertEqual("gear", row["mutationFamily"])
        self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
        self.assertEqual(
            "Build > Gear > Gear > selected stable root Gear > Stolen",
            row["phone"]["route"],
        )
        self.assertEqual("GearStolenPage", row["phone"]["surface"])
        self.assertEqual(
            "gear-stolen-toggle-{stable-root-gear-guid}",
            row["phone"]["automationId"],
        )
        self.assertIn("exact typed recursive Gear hierarchy", row["presenterMutation"])
        self.assertIn("Nuyen/Stolen eligibility", row["presenterMutation"])
        self.assertIn("character/gears/.../stolen", row["persistenceAssertion"])
        self.assertIn("revision-bound atomic save", row["persistenceAssertion"])
        self.assertIn("enabled, non-add-to-rating Nuyen/Stolen", row["phone"]["coverageLimit"])
        self.assertIn("CharacterCareer", row["phone"]["coverageLimit"])
        self.assertEqual("missing", row["tablet"]["status"])
        self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
        self.assertEqual(
            "tests/run_api36_gear_stolen_e2e.py",
            row["e2e"]["phone"]["ref"],
        )
        self.assertFalse(row["completionProven"])

        weapon_stolen = [
            row for row in rows
            if row["legacy"]["formOrControl"] == "CharacterCreate"
            and row["legacy"]["controlName"] == inventory.WEAPON_STOLEN_CONTROL
        ]
        self.assertEqual(1, len(weapon_stolen))
        row = weapon_stolen[0]
        self.assertEqual("weapons", row["mutationFamily"])
        self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
        self.assertEqual(
            "Build > Gear > Weapons > selected stable root Weapon > Stolen",
            row["phone"]["route"],
        )
        self.assertEqual("WeaponStolenPage", row["phone"]["surface"])
        self.assertEqual(
            "weapon-stolen-toggle-{stable-root-weapon-guid}",
            row["phone"]["automationId"],
        )
        self.assertIn("exact typed Weapon", row["presenterMutation"])
        self.assertIn("underbarrel Weapon", row["presenterMutation"])
        self.assertIn("WeaponAccessory", row["presenterMutation"])
        self.assertIn("recursive Gear hierarchy", row["presenterMutation"])
        self.assertIn("Nuyen/Stolen eligibility", row["presenterMutation"])
        self.assertIn("zero Nuyen/Karma economics", row["presenterMutation"])
        self.assertIn("character/weapons/.../stolen", row["persistenceAssertion"])
        self.assertIn("Nuyen, Karma", row["persistenceAssertion"])
        self.assertIn("revision-bound atomic save/recovery", row["persistenceAssertion"])
        self.assertIn("enabled, non-add-to-rating Nuyen/Stolen", row["phone"]["coverageLimit"])
        self.assertIn("CharacterCareer", row["phone"]["coverageLimit"])
        self.assertEqual("missing", row["tablet"]["status"])
        self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
        self.assertEqual(
            "tests/run_api36_weapon_stolen_e2e.py",
            row["e2e"]["phone"]["ref"],
        )
        self.assertFalse(row["completionProven"])

        gear_equipment = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] == inventory.GEAR_EQUIPMENT_CONTROL
        ]
        self.assertEqual(2, len(gear_equipment))
        self.assertEqual(
            {"CharacterCreate", "CharacterCareer"},
            {row["legacy"]["formOrControl"] for row in gear_equipment},
        )
        for row in gear_equipment:
            self.assertEqual("gear", row["mutationFamily"])
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual(
                "Build > Gear > Gear > selected stable root Gear > Equipped",
                row["phone"]["route"],
            )
            self.assertEqual("GearEquipmentPage", row["phone"]["surface"])
            self.assertEqual(
                "gear-equipment-toggle-{stable-root-gear-guid}",
                row["phone"]["automationId"],
            )
            self.assertIn("exact typed recursive Gear hierarchy", row["presenterMutation"])
            self.assertIn("Create/Career phase", row["presenterMutation"])
            self.assertIn("zero Nuyen/Karma economics", row["presenterMutation"])
            self.assertIn("character/gears/.../equipped", row["persistenceAssertion"])
            self.assertIn("Nuyen, Karma", row["persistenceAssertion"])
            self.assertIn("revision-bound atomic save/recovery", row["persistenceAssertion"])
            self.assertIn("IncludedInParent Gear is read-only", row["phone"]["coverageLimit"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_gear_equipment_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertFalse(row["completionProven"])

        gear_attack_swap = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] == inventory.GEAR_ATTACK_SWAP_CONTROL
        ]
        self.assertEqual(2, len(gear_attack_swap))
        self.assertEqual(
            {"CharacterCreate", "CharacterCareer"},
            {row["legacy"]["formOrControl"] for row in gear_attack_swap},
        )
        for row in gear_attack_swap:
            self.assertEqual("gear", row["mutationFamily"])
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual("GearAttackSwapPage", row["phone"]["surface"])
            self.assertEqual(
                "gear-attack-swap-attribute-{stable-root-gear-guid}",
                row["phone"]["automationId"],
            )
            self.assertIn("Create/Career phase", row["presenterMutation"])
            self.assertIn("raw saved base Matrix strings", row["presenterMutation"])
            self.assertIn("zero Nuyen/Karma economics", row["presenterMutation"])
            self.assertIn("attributearray/canswapattributes provenance", row["persistenceAssertion"])
            self.assertIn("bonuses", row["persistenceAssertion"])
            self.assertIn("revision-bound atomic save/recovery", row["persistenceAssertion"])
            self.assertIn("equal raw values", row["phone"]["coverageLimit"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual("tests/run_api36_gear_attack_swap_e2e.py", row["e2e"]["phone"]["ref"])
            self.assertFalse(row["completionProven"])

        gear_sleaze_swap = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] == inventory.GEAR_SLEAZE_SWAP_CONTROL
        ]
        self.assertEqual(2, len(gear_sleaze_swap))
        self.assertEqual({"CharacterCreate", "CharacterCareer"},
            {row["legacy"]["formOrControl"] for row in gear_sleaze_swap})
        for row in gear_sleaze_swap:
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual("GearSleazeSwapPage", row["phone"]["surface"])
            self.assertIn("explicit Sleaze-to-target raw Matrix swap", row["presenterMutation"])
            self.assertIn("zero Nuyen/Karma economics", row["presenterMutation"])
            self.assertIn("active/home notification consumers", row["persistenceAssertion"])
            self.assertIn("bonuses", row["persistenceAssertion"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual("tests/run_api36_gear_sleaze_swap_e2e.py", row["e2e"]["phone"]["ref"])
            self.assertEqual("missing", row["tablet"]["status"])

        gear_data_processing_firewall_swap = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"]
                in inventory.GEAR_DATA_PROCESSING_FIREWALL_SWAP_CONTROLS
        ]
        self.assertEqual(4, len(gear_data_processing_firewall_swap))
        self.assertEqual(
            {
                ("CharacterCreate", "cboGearDataProcessing"),
                ("CharacterCreate", "cboGearFirewall"),
                ("CharacterCareer", "cboGearDataProcessing"),
                ("CharacterCareer", "cboGearFirewall"),
            },
            {(row["legacy"]["formOrControl"], row["legacy"]["controlName"])
             for row in gear_data_processing_firewall_swap},
        )
        for row in gear_data_processing_firewall_swap:
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual("GearDataProcessingFirewallSwapPage", row["phone"]["surface"])
            self.assertIn("Data Processing-or-Firewall-to-target", row["presenterMutation"])
            self.assertIn("zero Nuyen/Karma economics", row["presenterMutation"])
            self.assertIn("active/home notification consumers and flags", row["persistenceAssertion"])
            self.assertIn("revision-bound atomic save/recovery", row["persistenceAssertion"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_gear_dp_firewall_swap_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertEqual("missing", row["tablet"]["status"])

        gear_overclocker = [
            row for row in rows
            if row["legacy"]["formOrControl"] == "CharacterCareer"
            and row["legacy"]["controlName"] == inventory.GEAR_OVERCLOCKER_CONTROL
        ]
        self.assertEqual(1, len(gear_overclocker))
        row = gear_overclocker[0]
        self.assertEqual("gear", row["mutationFamily"])
        self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
        self.assertEqual(
            "Build > Gear > Gear > selected stable root Gear > Career Cyberdeck Overclocker",
            row["phone"]["route"],
        )
        self.assertEqual("GearOverclockerPage", row["phone"]["surface"])
        self.assertEqual(
            "gear-overclocker-attribute-{stable-root-gear-guid}",
            row["phone"]["automationId"],
        )
        self.assertIn("exact recursive Gear Guid identity", row["presenterMutation"])
        self.assertIn("Career phase", row["presenterMutation"])
        self.assertIn("Overclocker Improvement", row["presenterMutation"])
        self.assertIn("Cyberdecks-category eligibility", row["presenterMutation"])
        self.assertIn("Data Processing", row["presenterMutation"])
        self.assertIn("zero Nuyen/Karma economics", row["presenterMutation"])
        self.assertIn("character/gears/.../overclocked", row["persistenceAssertion"])
        self.assertIn("active/home/equipped/stolen flags", row["persistenceAssertion"])
        self.assertIn("revision-bound atomic save/recovery", row["persistenceAssertion"])
        self.assertIn("CharacterCreate", row["phone"]["coverageLimit"])
        self.assertIn("missing/disabled eligibility", row["phone"]["coverageLimit"])
        self.assertEqual("missing", row["tablet"]["status"])
        self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
        self.assertEqual(
            "tests/run_api36_gear_overclocker_e2e.py",
            row["e2e"]["phone"]["ref"],
        )
        self.assertFalse(row["completionProven"])

        improvement_active = [
            row for row in rows
            if row["legacy"]["formOrControl"] == "CharacterCareer"
            and row["legacy"]["controlName"] == inventory.IMPROVEMENT_ACTIVE_CONTROL
        ]
        self.assertEqual(1, len(improvement_active))
        row = improvement_active[0]
        self.assertEqual("improvements", row["mutationFamily"])
        self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
        self.assertEqual("Build > Improvements > Active state", row["phone"]["route"])
        self.assertEqual("ImprovementActivePage", row["phone"]["surface"])
        self.assertEqual("improvement-active-toggle", row["phone"]["automationId"])
        self.assertIn("SourceName-anchored", row["presenterMutation"])
        self.assertIn("improvement/enabled", row["persistenceAssertion"])
        self.assertIn("numeric 1 or 0", row["persistenceAssertion"])
        self.assertIn("direct treImprovements node", row["phone"]["coverageLimit"])
        self.assertIn("CharacterCreate", row["phone"]["coverageLimit"])
        self.assertEqual("missing", row["tablet"]["status"])
        self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
        self.assertEqual(
            "tests/run_api36_improvement_active_e2e.py",
            row["e2e"]["phone"]["ref"],
        )
        self.assertFalse(row["completionProven"])

        improvement_notes = [
            row for row in rows
            if row["legacy"]["formOrControl"] == "CharacterCareer"
            and row["legacy"]["controlName"] == inventory.IMPROVEMENT_NOTES_CONTROL
        ]
        self.assertEqual(1, len(improvement_notes))
        row = improvement_notes[0]
        self.assertEqual("improvements", row["mutationFamily"])
        self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
        self.assertEqual(
            "Build > Improvement Notes > selected saved Improvement",
            row["phone"]["route"],
        )
        self.assertEqual("ImprovementNotesPage", row["phone"]["surface"])
        self.assertEqual("improvement-notes-text", row["phone"]["automationId"])
        self.assertIn("SourceName-anchored", row["presenterMutation"])
        self.assertIn("notes-and-color item-local revision", row["presenterMutation"])
        self.assertIn("notes and notesColor together", row["persistenceAssertion"])
        self.assertIn("enabled state", row["persistenceAssertion"])
        self.assertIn("direct treImprovements IHasNotes node", row["phone"]["coverageLimit"])
        self.assertIn("Chocolate default", row["phone"]["coverageLimit"])
        self.assertIn("CharacterCreate", row["phone"]["coverageLimit"])
        self.assertEqual("missing", row["tablet"]["status"])
        self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
        self.assertEqual(
            "tests/run_api36_improvement_notes_e2e.py",
            row["e2e"]["phone"]["ref"],
        )
        self.assertFalse(row["completionProven"])

        improvement_group_add = next(
            row for row in rows
            if row["legacy"]["formOrControl"] == "CharacterCareer"
            and row["legacy"]["controlName"] == inventory.IMPROVEMENT_GROUP_ADD_CONTROL
        )
        self.assertEqual("improvements", improvement_group_add["mutationFamily"])
        self.assertEqual("implemented_pending_emulator", improvement_group_add["phone"]["status"])
        self.assertEqual("Build > Add Improvement Group", improvement_group_add["phone"]["route"])
        self.assertEqual("ImprovementGroupAddPage", improvement_group_add["phone"]["surface"])
        self.assertEqual("improvement-group-add-name", improvement_group_add["phone"]["automationId"])
        self.assertIn("expected-append-index", improvement_group_add["presenterMutation"])
        self.assertIn("zero Karma/Nuyen", improvement_group_add["presenterMutation"])
        self.assertIn("untrimmed value is appended", improvement_group_add["persistenceAssertion"])
        self.assertIn("order and duplicates are preserved", improvement_group_add["persistenceAssertion"])
        self.assertIn("cancel or empty text is a no-op", improvement_group_add["phone"]["coverageLimit"])
        self.assertIn("CharacterCreate", improvement_group_add["phone"]["coverageLimit"])
        self.assertEqual("missing", improvement_group_add["tablet"]["status"])
        self.assertEqual("scripted_not_executed", improvement_group_add["e2e"]["phone"]["status"])
        self.assertEqual(
            "tests/run_api36_improvement_group_add_e2e.py",
            improvement_group_add["e2e"]["phone"]["ref"],
        )
        self.assertFalse(improvement_group_add["completionProven"])

        free_sprite_conversion = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] == inventory.FREE_SPRITE_CONVERSION_CONTROL
        ]
        self.assertEqual(2, len(free_sprite_conversion))
        for row in free_sprite_conversion:
            self.assertEqual("spirits_and_sprites", row["mutationFamily"])
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual("Build > Convert to Free Sprite", row["phone"]["route"])
            self.assertEqual("FreeSpriteConversionPage", row["phone"]["surface"])
            self.assertEqual("free-sprite-conversion-save", row["phone"]["automationId"])
            self.assertIn("exact Denial source identity", row["presenterMutation"])
            self.assertIn("zero Karma/Nuyen", row["presenterMutation"])
            self.assertIn("counttowardslimit False", row["persistenceAssertion"])
            self.assertIn("identical zero-cost rules", row["phone"]["coverageLimit"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_free_sprite_conversion_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertFalse(row["completionProven"])

        martial_art_notes = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] == inventory.MARTIAL_ART_NOTES_CONTROL
        ]
        self.assertEqual(2, len(martial_art_notes))
        for row in martial_art_notes:
            self.assertEqual("qualities", row["mutationFamily"])
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual(
                "Build > Martial Arts Notes > selected Martial Art or parent-scoped Technique",
                row["phone"]["route"],
            )
            self.assertEqual("MartialArtNotesPage", row["phone"]["surface"])
            self.assertEqual("martial-art-notes-save", row["phone"]["automationId"])
            self.assertIn("parent-scoped Technique", row["presenterMutation"])
            self.assertIn("all non-notes target fields", row["persistenceAssertion"])
            self.assertIn("zero Karma/Nuyen", row["phone"]["coverageLimit"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_martial_art_notes_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertFalse(row["completionProven"])

        martial_art_delete = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] == inventory.MARTIAL_ART_DELETE_CONTROL
        ]
        self.assertEqual(2, len(martial_art_delete))
        for row in martial_art_delete:
            self.assertEqual("qualities", row["mutationFamily"])
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual(
                "Build > Delete Martial Art > selected non-quality Art or parent-scoped Technique",
                row["phone"]["route"],
            )
            self.assertEqual("MartialArtDeletePage", row["phone"]["surface"])
            self.assertEqual("martial-art-delete-confirm", row["phone"]["automationId"])
            self.assertIn("explicit confirmation", row["presenterMutation"])
            self.assertIn("improvementsource plus sourcename GUID", row["persistenceAssertion"])
            self.assertIn("no Karma/Nuyen refund", row["phone"]["coverageLimit"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_martial_art_delete_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertFalse(row["completionProven"])

        improvement_group_active = [
            row for row in rows
            if row["legacy"]["formOrControl"] == "CharacterCareer"
            and row["legacy"]["controlName"] in inventory.IMPROVEMENT_GROUP_ACTIVE_CONTROLS
        ]
        self.assertEqual(2, len(improvement_group_active))
        for row in improvement_group_active:
            _, automation_id = inventory.IMPROVEMENT_GROUP_ACTIVE_CONTROLS[
                row["legacy"]["controlName"]
            ]
            self.assertEqual("improvements", row["mutationFamily"])
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual(
                "Build > Improvement groups > selected saved custom group",
                row["phone"]["route"],
            )
            self.assertEqual("ImprovementGroupActivePage", row["phone"]["surface"])
            self.assertEqual(automation_id, row["phone"]["automationId"])
            self.assertIn("typed ungrouped/named", row["presenterMutation"])
            self.assertIn("only opposite-state custom improvements", row["persistenceAssertion"])
            self.assertIn("Node_SelectedImprovements", row["phone"]["coverageLimit"])
            self.assertIn("CharacterCreate", row["phone"]["coverageLimit"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_improvement_group_active_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertFalse(row["completionProven"])

        weapon_accessory_included = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] == inventory.WEAPON_ACCESSORY_INCLUDED_CONTROL
        ]
        self.assertEqual(2, len(weapon_accessory_included))
        for row in weapon_accessory_included:
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual(
                "Build > Gear > Weapon Accessories > selected stable accessory > Included in Weapon",
                row["phone"]["route"],
            )
            self.assertEqual("WeaponAccessoryIncludedPage", row["phone"]["surface"])
            self.assertEqual(
                "weapon-accessory-included-toggle-{stable-accessory-guid}",
                row["phone"]["automationId"],
            )
            self.assertIn("stable parent weapon Guid", row["presenterMutation"])
            self.assertIn("stable accessory Guid", row["presenterMutation"])
            self.assertIn("sibling accessories", row["persistenceAssertion"])
            self.assertIn("atomic save", row["persistenceAssertion"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_weapon_accessory_included_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertFalse(row["completionProven"])

        critter_power_count = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] == inventory.CRITTER_POWER_COUNT_CONTROL
        ]
        self.assertEqual(2, len(critter_power_count))
        for row in critter_power_count:
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual(
                "Build > Critter > Critter Powers > selected stable power > Counts towards limit",
                row["phone"]["route"],
            )
            self.assertEqual("CritterPowerCountPage", row["phone"]["surface"])
            self.assertEqual(
                "critter-power-count-toggle-{stable-critter-power-guid}",
                row["phone"]["automationId"],
            )
            self.assertIn("stable CritterPower Guid", row["presenterMutation"])
            self.assertIn("expected content revision", row["presenterMutation"])
            self.assertIn("both directions", row["persistenceAssertion"])
            self.assertIn("atomic save", row["persistenceAssertion"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_critter_power_count_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertFalse(row["completionProven"])

        sustained_effects = [
            row for row in rows
            if row["legacy"]["formOrControl"] == "SustainedObjectControl"
            and row["legacy"]["controlName"] in inventory.SUSTAINED_EFFECTS_CONTROLS
        ]
        self.assertEqual(4, len(sustained_effects))
        for row in sustained_effects:
            control = row["legacy"]["controlName"]
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual(
                "Build > Runner > Sustained effects > selected saved occurrence",
                row["phone"]["route"],
            )
            self.assertEqual("SustainedObjectEditPage", row["phone"]["surface"])
            self.assertEqual(
                inventory.SUSTAINED_EFFECTS_CONTROLS[control],
                row["phone"]["automationId"],
            )
            self.assertIn("linked type + linked GUID + saved occurrence", row["presenterMutation"])
            self.assertIn("expected content revision", row["presenterMutation"])
            self.assertIn("exact duplicate occurrence", row["persistenceAssertion"])
            self.assertIn("confirmed delete", row["persistenceAssertion"])
            self.assertIn("atomic save", row["persistenceAssertion"])
            self.assertIn("runtime GUID", row["phone"]["coverageLimit"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_sustained_effects_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertFalse(row["completionProven"])

        psyche_active = [
            row for row in rows
            if row["legacy"]["formOrControl"] == "CharacterCareer"
            and row["legacy"]["controlName"] in inventory.PSYCHE_ACTIVE_CONTROLS
        ]
        self.assertEqual(2, len(psyche_active))
        for row in psyche_active:
            surface, automation_id = inventory.PSYCHE_ACTIVE_CONTROLS[
                row["legacy"]["controlName"]
            ]
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual(
                "Build > Runner > Sustained effects > Psyche",
                row["phone"]["route"],
            )
            self.assertEqual("SustainedObjectsPage", row["phone"]["surface"])
            self.assertEqual(automation_id, row["phone"]["automationId"])
            self.assertIn(f"CharacterPsycheActiveSurface.{surface}", row["presenterMutation"])
            self.assertIn("shared saved Psyche state", row["presenterMutation"])
            self.assertIn("one root character/psyche Boolean", row["persistenceAssertion"])
            self.assertIn("same Character.PsycheActive value", row["phone"]["coverageLimit"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual("tests/run_api36_psyche_active_e2e.py", row["e2e"]["phone"]["ref"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertFalse(row["completionProven"])

        gear_quantities = [
            row for row in rows
            if row["legacy"]["formOrControl"] == "CharacterCareer"
            and row["legacy"]["controlName"] in inventory.GEAR_QUANTITY_CONTROLS
        ]
        self.assertEqual(4, len(gear_quantities))
        self.assertFalse(any(
            row["legacy"]["formOrControl"] == "CharacterCreate"
            and row["legacy"]["controlName"] in inventory.GEAR_QUANTITY_CONTROLS
            for row in rows
        ))
        for row in gear_quantities:
            action, automation_id = inventory.GEAR_QUANTITY_CONTROLS[
                row["legacy"]["controlName"]
            ]
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual(
                "Build > Gear > Gear > selected stable Career Gear > Gear Quantity",
                row["phone"]["route"],
            )
            self.assertEqual("GearQuantityPage", row["phone"]["surface"])
            self.assertEqual(automation_id, row["phone"]["automationId"])
            self.assertIn(f"GearQuantityEditRequest.{action}", row["presenterMutation"])
            self.assertIn("stable top-level gear Guid", row["presenterMutation"])
            self.assertIn("exact decimal quantity", row["persistenceAssertion"])
            self.assertIn("atomic save", row["persistenceAssertion"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_gear_quantity_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertFalse(row["completionProven"])

        cyberware_commerce = [
            row for row in rows
            if row["legacy"]["formOrControl"] == "CharacterCareer"
            and row["legacy"]["controlName"] in inventory.CYBERWARE_COMMERCE_CONTROLS
        ]
        self.assertEqual(2, len(cyberware_commerce))
        self.assertFalse(any(
            row["legacy"]["formOrControl"] == "CharacterCreate"
            and row["legacy"]["controlName"] in inventory.CYBERWARE_COMMERCE_CONTROLS
            for row in rows
        ))
        for row in cyberware_commerce:
            action, automation_id = inventory.CYBERWARE_COMMERCE_CONTROLS[
                row["legacy"]["controlName"]
            ]
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual(
                "Build > Gear > Cyberwares > selected stable Career Cyberware > Upgrade or Sell",
                row["phone"]["route"],
            )
            self.assertEqual("CyberwareCommercePage", row["phone"]["surface"])
            self.assertEqual(automation_id, row["phone"]["automationId"])
            self.assertIn(f"CyberwareCommerceRequest.{action}", row["presenterMutation"])
            self.assertIn("quote digest", row["presenterMutation"])
            self.assertIn("explicit confirmation", row["presenterMutation"])
            self.assertIn("Essence Hole", row["persistenceAssertion"])
            self.assertIn("atomic save", row["persistenceAssertion"])
            self.assertIn("Capacity=[*]", row["phone"]["coverageLimit"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_cyberware_commerce_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertFalse(row["completionProven"])

        location_renames = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] in inventory.LOCATION_RENAME_CONTROLS
        ]
        self.assertEqual(
            {
                (form, control)
                for form in ("CharacterCreate", "CharacterCareer")
                for control in inventory.LOCATION_RENAME_CONTROLS
            },
            {
                (row["legacy"]["formOrControl"], row["legacy"]["controlName"])
                for row in location_renames
            },
        )
        for row in location_renames:
            kind, section_id = inventory.LOCATION_RENAME_CONTROLS[
                row["legacy"]["controlName"]
            ]
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual(
                f"Build > Gear > {kind} Locations > selected stable location > Rename",
                row["phone"]["route"],
            )
            self.assertEqual("LocationRenamePage", row["phone"]["surface"])
            self.assertEqual("location-rename-save", row["phone"]["automationId"])
            self.assertIn(f"WorkspaceLocationKind.{kind}", row["presenterMutation"])
            self.assertIn("stable Guid identity", row["presenterMutation"])
            self.assertIn(f"character/{section_id}/location", row["persistenceAssertion"])
            self.assertIn("same-session reopen", row["persistenceAssertion"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_location_rename_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertFalse(row["completionProven"])

        explicit_save = [
            row for row in rows
            if (
                row["legacy"]["formOrControl"],
                row["legacy"]["controlName"],
            ) in inventory.EXPLICIT_SAVE_CONTROLS
        ]
        self.assertEqual(5, len(explicit_save))
        for row in explicit_save:
            route, surface, automation_id = inventory.EXPLICIT_SAVE_CONTROLS[
                (
                    row["legacy"]["formOrControl"],
                    row["legacy"]["controlName"],
                )
            ]
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual(route, row["phone"]["route"])
            self.assertEqual(surface, row["phone"]["surface"])
            self.assertEqual(automation_id, row["phone"]["automationId"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_explicit_save_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertIn("SavedRevision equals ContentRevision", row["persistenceAssertion"])
            self.assertFalse(row["completionProven"])
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
        dynamic_character_condition_rows = [
            row for row in rows
            if row["legacy"]["formOrControl"] == "CharacterCareer"
            and row["legacy"]["controlName"]
            == inventory.DYNAMIC_CHARACTER_CONDITION_CONTROL
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
        self.assertEqual(1, len(dynamic_character_condition_rows))
        self.assertEqual(4, len(dashboard_condition_rows))
        self.assertEqual(24, len(vehicle_physical_rows))
        self.assertEqual(18, len(contact_rows))
        self.assertEqual(4, len(pet_rows))
        dynamic_condition = dynamic_character_condition_rows[0]
        self.assertEqual(
            "implemented_verified_api36",
            dynamic_condition["phone"]["status"],
        )
        self.assertEqual(
            "implemented_verified_api36",
            dynamic_condition["tablet"]["status"],
        )
        self.assertEqual(
            "condition-monitor-filled-{physical|stun}",
            dynamic_condition["phone"]["automationId"],
        )
        self.assertEqual(
            "tablet-condition-filled-{physical|stun}",
            dynamic_condition["tablet"]["automationId"],
        )
        self.assertEqual("executed_api36", dynamic_condition["e2e"]["phone"]["status"])
        self.assertEqual("executed_api36", dynamic_condition["e2e"]["tablet"]["status"])
        self.assertIn("Physical / Stun", dynamic_condition["phone"]["route"])
        self.assertIn("physicalcmfilled", dynamic_condition["persistenceAssertion"])
        self.assertIn("stuncmfilled", dynamic_condition["persistenceAssertion"])
        self.assertIn("single synthetic Chummer5 runtime row", dynamic_condition["phone"]["coverageLimit"])
        self.assertTrue(dynamic_condition["completionProven"])
        linked_character_rows = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"ContactControl", "PetControl"}
            and row["legacy"]["controlName"] in {"tsAttachCharacter", "tsRemoveCharacter"}
        ]
        self.assertEqual(4, len(linked_character_rows))
        spirit_linked_runner_rows = [
            row for row in rows
            if row["legacy"]["formOrControl"] == "SpiritControl"
            and row["legacy"]["controlName"] in inventory.SPIRIT_LINKED_RUNNER_CONTROLS
        ]
        self.assertEqual(
            set(inventory.SPIRIT_LINKED_RUNNER_CONTROLS),
            {row["legacy"]["controlName"] for row in spirit_linked_runner_rows},
        )
        for row in spirit_linked_runner_rows:
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("missing", row["e2e"]["phone"]["status"])
            self.assertIn("Spirits and sprites", row["phone"]["route"])
            self.assertIn("WorkspaceCollectionKind.Spirit", row["presenterMutation"])
            self.assertIn("stable Spirit or Sprite guid", row["persistenceAssertion"])
        character_collection_delete_rows = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] in inventory.LEGACY_CHARACTER_COLLECTION_DELETE_CONTROLS
        ]
        self.assertEqual(
            {
                (form_name, control)
                for form_name in ("CharacterCreate", "CharacterCareer")
                for control in inventory.LEGACY_CHARACTER_COLLECTION_DELETE_CONTROLS
            },
            {
                (row["legacy"]["formOrControl"], row["legacy"]["controlName"])
                for row in character_collection_delete_rows
            },
        )
        for row in character_collection_delete_rows:
            kind, section_label = inventory.LEGACY_CHARACTER_COLLECTION_DELETE_CONTROLS[
                row["legacy"]["controlName"]
            ]
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("missing", row["e2e"]["phone"]["status"])
            self.assertIn(section_label, row["phone"]["route"])
            self.assertIn(f"WorkspaceCollectionKind.{kind}", row["presenterMutation"])
            self.assertIn(kind, row["persistenceAssertion"])
            self.assertFalse(row["completionProven"])
        character_collection_notes_rows = [
            row for row in rows
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] in inventory.LEGACY_CHARACTER_COLLECTION_NOTES_CONTROLS
        ]
        self.assertEqual(
            {
                (form_name, control)
                for form_name in ("CharacterCreate", "CharacterCareer")
                for control in inventory.LEGACY_CHARACTER_COLLECTION_NOTES_CONTROLS
            },
            {
                (row["legacy"]["formOrControl"], row["legacy"]["controlName"])
                for row in character_collection_notes_rows
            },
        )
        for row in character_collection_notes_rows:
            kind, section_label, _ = inventory.LEGACY_CHARACTER_COLLECTION_NOTES_CONTROLS[
                row["legacy"]["controlName"]
            ]
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("missing", row["e2e"]["phone"]["status"])
            self.assertEqual("collection-field-notes-{stable-target}", row["phone"]["automationId"])
            self.assertIn(section_label, row["phone"]["route"])
            self.assertIn(f"WorkspaceCollectionKind.{kind}", row["presenterMutation"])
            self.assertIn("after save, reopen, and process restart", row["persistenceAssertion"])
            self.assertFalse(row["completionProven"])
        nested_collection_notes_rows = [
            row for row in rows
            if row["legacy"]["controlName"] in inventory.LEGACY_NESTED_COLLECTION_NOTES_CONTROLS
            and row["legacy"]["formOrControl"]
                in inventory.LEGACY_NESTED_COLLECTION_NOTES_CONTROLS[
                    row["legacy"]["controlName"]
                ][5]
        ]
        expected_nested_notes = {
            (form_name, control)
            for control, values in inventory.LEGACY_NESTED_COLLECTION_NOTES_CONTROLS.items()
            for form_name in values[5]
        }
        self.assertEqual(
            expected_nested_notes,
            {
                (row["legacy"]["formOrControl"], row["legacy"]["controlName"])
                for row in nested_collection_notes_rows
            },
        )
        self.assertEqual(5, len(nested_collection_notes_rows))
        for row in nested_collection_notes_rows:
            kind, nested_kind, section_label, *_ = (
                inventory.LEGACY_NESTED_COLLECTION_NOTES_CONTROLS[
                    row["legacy"]["controlName"]
                ]
            )
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "collection-field-notes-{stable-nested-target}",
                row["phone"]["automationId"],
            )
            self.assertIn(section_label, row["phone"]["route"])
            self.assertIn(f"WorkspaceCollectionKind.{kind}", row["presenterMutation"])
            self.assertIn(
                f"WorkspaceNestedCollectionKind.{nested_kind}",
                row["presenterMutation"],
            )
            self.assertIn("parent+child guid pair", row["persistenceAssertion"])
            self.assertFalse(row["completionProven"])
        character_collection_text_rows = [
            row for row in rows
            if row["legacy"]["controlName"] in inventory.LEGACY_CHARACTER_COLLECTION_TEXT_CONTROLS
            and row["legacy"]["formOrControl"]
                in inventory.LEGACY_CHARACTER_COLLECTION_TEXT_CONTROLS[
                    row["legacy"]["controlName"]
                ][5]
        ]
        expected_collection_text = {
            (form_name, control)
            for control, (_, _, _, _, _, form_names)
                in inventory.LEGACY_CHARACTER_COLLECTION_TEXT_CONTROLS.items()
            for form_name in form_names
        }
        self.assertEqual(
            expected_collection_text,
            {
                (row["legacy"]["formOrControl"], row["legacy"]["controlName"])
                for row in character_collection_text_rows
            },
        )
        self.assertEqual(7, len(character_collection_text_rows))
        for row in character_collection_text_rows:
            kind, section_label, field, xml_element, _, _ = (
                inventory.LEGACY_CHARACTER_COLLECTION_TEXT_CONTROLS[
                    row["legacy"]["controlName"]
                ]
            )
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("missing", row["e2e"]["phone"]["status"])
            self.assertEqual(
                f"collection-field-{field.lower()}-{{stable-target}}",
                row["phone"]["automationId"],
            )
            self.assertIn(section_label, row["phone"]["route"])
            self.assertIn(f"WorkspaceCollectionKind.{kind}", row["presenterMutation"])
            self.assertIn(f"WorkspaceCollectionTextField.{field}", row["presenterMutation"])
            self.assertIn(xml_element, row["persistenceAssertion"])
            self.assertIn("process restart", row["persistenceAssertion"])
            self.assertFalse(row["completionProven"])
        character_collection_toggle_rows = [
            row for row in rows
            if row["legacy"]["controlName"] in inventory.LEGACY_CHARACTER_COLLECTION_TOGGLE_CONTROLS
            and row["legacy"]["controlName"]
                not in {
                    "chkArmorEquipped",
                    inventory.GEAR_EQUIPMENT_CONTROL,
                    inventory.GEAR_WIRELESS_CONTROL,
                }
            and row["legacy"]["formOrControl"]
                in inventory.LEGACY_CHARACTER_COLLECTION_TOGGLE_CONTROLS[
                    row["legacy"]["controlName"]
                ][4]
        ]
        expected_collection_toggles = {
            (form_name, control)
            for control, (_, _, _, _, form_names)
                in inventory.LEGACY_CHARACTER_COLLECTION_TOGGLE_CONTROLS.items()
            if control not in {
                "chkArmorEquipped",
                inventory.GEAR_EQUIPMENT_CONTROL,
                inventory.GEAR_WIRELESS_CONTROL,
            }
            for form_name in form_names
        }
        self.assertEqual(
            expected_collection_toggles,
            {
                (row["legacy"]["formOrControl"], row["legacy"]["controlName"])
                for row in character_collection_toggle_rows
            },
        )
        self.assertEqual(15, len(character_collection_toggle_rows))
        for row in character_collection_toggle_rows:
            kind, section_label, field, xml_element, _ = (
                inventory.LEGACY_CHARACTER_COLLECTION_TOGGLE_CONTROLS[
                    row["legacy"]["controlName"]
                ]
            )
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("missing", row["e2e"]["phone"]["status"])
            self.assertEqual(
                f"collection-toggle-{field.lower()}-{{stable-target}}",
                row["phone"]["automationId"],
            )
            self.assertIn(section_label, row["phone"]["route"])
            self.assertIn(f"WorkspaceCollectionKind.{kind}", row["presenterMutation"])
            self.assertIn(xml_element, row["persistenceAssertion"])
            self.assertFalse(row["completionProven"])
        creation_collection_numeric_rows = [
            row for row in rows
            if row["legacy"]["formOrControl"] == "CharacterCreate"
            and row["legacy"]["controlName"] in inventory.LEGACY_CREATION_COLLECTION_NUMERIC_CONTROLS
        ]
        self.assertEqual(
            set(inventory.LEGACY_CREATION_COLLECTION_NUMERIC_CONTROLS),
            {row["legacy"]["controlName"] for row in creation_collection_numeric_rows},
        )
        self.assertEqual(5, len(creation_collection_numeric_rows))
        for row in creation_collection_numeric_rows:
            kind, section_label, numeric_kind, xml_element = (
                inventory.LEGACY_CREATION_COLLECTION_NUMERIC_CONTROLS[
                    row["legacy"]["controlName"]
                ]
            )
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("missing", row["e2e"]["phone"]["status"])
            self.assertEqual(
                f"collection-{numeric_kind.lower()}-{{stable-target}}",
                row["phone"]["automationId"],
            )
            self.assertIn(section_label, row["phone"]["route"])
            self.assertIn(f"WorkspaceCollectionKind.{kind}", row["presenterMutation"])
            self.assertIn(xml_element, row["persistenceAssertion"])
            self.assertFalse(row["completionProven"])
        spirit_rows = {
            row["legacy"]["controlName"]: row
            for row in rows
            if row["legacy"]["formOrControl"] == "SpiritControl"
            and row["legacy"]["controlName"] in inventory.SPIRIT_GENERIC_EDITOR_CONTROLS
        }
        self.assertEqual(set(inventory.SPIRIT_GENERIC_EDITOR_CONTROLS), set(spirit_rows))
        for control, (editor_kind, field, token) in inventory.SPIRIT_GENERIC_EDITOR_CONTROLS.items():
            row = spirit_rows[control]
            self.assertEqual(
                "partial_exact_saved_data"
                if editor_kind in {"force", "critter"}
                else "implemented_pending_emulator",
                row["phone"]["status"],
            )
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("missing", row["e2e"]["phone"]["status"])
            self.assertIn("Spirits and sprites", row["phone"]["route"])
            self.assertIn("WorkspaceCollectionKind.Spirit", row["presenterMutation"])
            self.assertIn("stable Spirit guid", row["persistenceAssertion"])
            self.assertFalse(row["completionProven"])
            if editor_kind in {"text", "critter"}:
                self.assertEqual(
                    f"collection-field-{token}-{{stable-target}}",
                    row["phone"]["automationId"],
                )
            elif editor_kind == "toggle":
                self.assertEqual(
                    f"collection-toggle-{token}-{{stable-target}}",
                    row["phone"]["automationId"],
                )
            elif editor_kind in {"integer", "force"}:
                self.assertEqual(
                    f"collection-integer-{token}-{{stable-target}}",
                    row["phone"]["automationId"],
                )
            else:
                self.assertEqual("collection-delete-{stable-target}", row["phone"]["automationId"])
        name_choice_rows = [
            row for row in rows
            if row["legacy"]["formOrControl"] == "SpiritControl"
            and row["legacy"]["controlName"] == "cboSpiritName"
        ]
        self.assertEqual(1, len(name_choice_rows), "the shared selector must remain one legacy authority row")
        name_choice = name_choice_rows[0]
        self.assertEqual("partial_exact_saved_data", name_choice["phone"]["status"])
        self.assertEqual("SpiritNameChoicePage", name_choice["phone"]["surface"])
        self.assertEqual(
            "spirit-name-choice-picker-{stable-spirit-guid}",
            name_choice["phone"]["automationId"],
        )
        self.assertIn("Spirit/Sprite metatype", name_choice["phone"]["route"])
        self.assertIn("ApplySpiritNameChoiceEditAsync", name_choice["presenterMutation"])
        self.assertIn("direct name", name_choice["persistenceAssertion"])
        self.assertIn("One shared SpiritControl.cboSpiritName row", name_choice["phone"]["coverageLimit"])
        self.assertIn("All expands", name_choice["phone"]["coverageLimit"])
        self.assertIn("present but not yet executed", name_choice["phone"]["coverageLimit"])
        self.assertEqual("missing", name_choice["e2e"]["phone"]["status"])
        self.assertEqual("missing", name_choice["tablet"]["status"])
        self.assertFalse(name_choice["completionProven"])
        fettered_rows = [
            row for row in rows
            if row["legacy"]["formOrControl"] == "SpiritControl"
            and row["legacy"]["controlName"] == "chkFettered"
        ]
        self.assertEqual(1, len(fettered_rows), "the shared control must remain one legacy authority row")
        fettered = fettered_rows[0]
        self.assertEqual("partial_exact_saved_data", fettered["phone"]["status"])
        self.assertEqual("SpiritFetteredPage", fettered["phone"]["surface"])
        self.assertEqual("spirit-fettered-toggle-{stable-target}", fettered["phone"]["automationId"])
        self.assertIn("Fettered Spirit / Sprite Pet", fettered["phone"]["route"])
        self.assertIn("ApplySpiritFetteredEditAsync", fettered["presenterMutation"])
        self.assertIn("Create and Career", fettered["persistenceAssertion"])
        self.assertIn("KarmaSpiritFettering", fettered["phone"]["coverageLimit"])
        self.assertIn("present but not yet executed", fettered["phone"]["coverageLimit"])
        self.assertEqual("missing", fettered["e2e"]["phone"]["status"])
        self.assertEqual("missing", fettered["tablet"]["status"])
        self.assertFalse(fettered["completionProven"])
        self.assertTrue(all(row["presenterMutation"] for row in origin_rows + attribute_rows))
        for row in origin_rows:
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_origin_dossier_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("missing", row["e2e"]["tablet"]["status"])
            self.assertFalse(row["completionProven"])
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
            self.assertEqual("implemented_verified_api36", row["phone"]["status"])
            self.assertEqual("executed_api36", row["e2e"]["phone"]["status"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("missing", row["e2e"]["tablet"]["status"])
            self.assertFalse(row["completionProven"])
        self.assertTrue(all(row["presenterMutation"] for row in matrix_rows))
        condition_rows = (
            character_condition_rows
            + dynamic_character_condition_rows
            + dashboard_condition_rows
            + vehicle_physical_rows
        )
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
        scripted_condition_rows = (
            character_condition_rows
            + dynamic_character_condition_rows
            + dashboard_condition_rows
        )
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
                and not row["completionProven"]
                for row in linked_character_rows
            )
        )
        self.assertEqual(
            {
                "implemented_pending_emulator": 456,
                "implemented_verified_api36": 79,
                "missing": 942,
                "not_applicable_non_mutating": 474,
                "partial_create_only": 106,
                "partial_exact_saved_data": 172,
            },
            payload["summary"]["phoneStatusCounts"],
        )
        self.assertEqual(
            {
                "implemented_pending_emulator": 4,
                "implemented_verified_api36": 75,
                "missing": 1532,
                "not_applicable_non_mutating": 474,
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

    def test_vehicle_active_commlink_phone_mapping_is_exact_phone_only_and_scripted(self) -> None:
        payload = json.loads(
            (REPO / "docs" / "ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json").read_text(
                encoding="utf-8"
            )
        )
        rows = [
            row for row in payload["rows"]
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] == inventory.VEHICLE_ACTIVE_COMMLINK_CONTROL
        ]
        self.assertEqual(2, len(rows))
        for row in rows:
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual(
                "Build > Gear > Vehicles > selected stable top-level persona-capable Vehicle > Vehicle Active Commlink",
                row["phone"]["route"],
            )
            self.assertEqual("VehicleActiveCommlinkPage", row["phone"]["surface"])
            self.assertEqual(
                "vehicle-active-commlink-toggle-{stable-vehicle-guid}",
                row["phone"]["automationId"],
            )
            self.assertIn("phase/persona/enabled/zero-economics", row["presenterMutation"])
            self.assertIn("revision-checked atomic save", row["persistenceAssertion"])
            self.assertIn("process restart recovery", row["persistenceAssertion"])
            self.assertIn("descendants fail closed", row["phone"]["coverageLimit"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_vehicle_active_commlink_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertFalse(row["completionProven"])

    def test_vehicle_active_commlink_receipt_is_strict_full_graph_fixture_and_proof_bound(self) -> None:
        presentation_root = PRESENTATION_ROOT
        core_root = CORE_ROOT
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        native_root = REPO / "src" / "Chummer.Android" / "Native"
        driver = REPO / "tests" / "run_api36_vehicle_active_commlink_e2e.py"
        shared_driver = REPO / "tests" / "run_api36_editing_e2e.py"
        creation_fixture = REPO / "tests" / "fixtures" / "creation-vehicle-active-commlink-e2e.chum5"
        career_fixture = REPO / "tests" / "fixtures" / "career-vehicle-active-commlink-e2e.chum5"
        source_paths = {
            "vehicleActiveCommlinkPageSha256": native_root / "VehicleActiveCommlinkPage.cs",
            "collectionEditorPagesSha256": native_root / "CollectionEditorPages.cs",
            "coordinatorSha256": native_root / "RunnerSessionCoordinator.cs",
            "vehicleActiveCommlinkContractSha256": overview / "VehicleActiveCommlinkEditRequest.cs",
            "collectionEditorStateSha256": overview / "WorkspaceCollectionEditorState.cs",
            "collectionEditorProjectorSha256": overview / "WorkspaceCollectionEditorProjector.cs",
            "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
            "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
            "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
            "presenterPersistenceSha256": overview / "CharacterOverviewPresenter.Persistence.cs",
            "vehicleActiveCommlinkRulesSha256": core_root / "Chummer.Contracts" / "Characters" / "CharacterVehicleActiveCommlinkRules.cs",
            "weaponHomeNodeRulesSha256": core_root / "Chummer.Contracts" / "Characters" / "CharacterWeaponHomeNodeRules.cs",
            "characterSectionModelsSha256": core_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
            "characterSectionServiceSha256": core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
            "workspaceStoreSha256": core_root / "Chummer.Infrastructure" / "Workspaces" / "FileWorkspaceStore.cs",
            "legacyCreateHandlerSha256": presentation_root / "Chummer" / "Forms" / "Character Forms" / "CharacterCreate.cs",
            "legacyCareerHandlerSha256": presentation_root / "Chummer" / "Forms" / "Character Forms" / "CharacterCareer.cs",
            "legacyMatrixAttributesSha256": presentation_root / "Chummer" / "Backend" / "Interfaces" / "IHasMatrixAttributes.cs",
            "legacyVehicleRulesSha256": presentation_root / "Chummer" / "Backend" / "Equipment" / "Vehicle.cs",
        }
        controls = {
            f"{form}.{inventory.VEHICLE_ACTIVE_COMMLINK_CONTROL}": {
                key: "pass"
                for key in inventory.VEHICLE_ACTIVE_COMMLINK_CONTROL_E2E_PROOF_KEYS
            }
            for form in ("CharacterCreate", "CharacterCareer")
        }
        receipt = {
            "schema": "chummer.android.editing-e2e/v1",
            "status": "pass",
            "profile": "phone",
            "journey": "vehicle-active-commlink",
            "apiLevel": 36,
            "abi": inventory.PHONE_E2E_ABI,
            "package": inventory.PHONE_E2E_PACKAGE,
            "apkSha256": "a" * 64,
            "driverSha256": inventory._sha256_file(driver),
            "sharedDriverSha256": inventory._sha256_file(shared_driver),
            "creationFixtureSha256": inventory._sha256_file(creation_fixture),
            "careerFixtureSha256": inventory._sha256_file(career_fixture),
            "controlCount": len(controls),
            "controls": controls,
            **{key: inventory._sha256_file(path) for key, path in source_paths.items()},
        }
        with tempfile.TemporaryDirectory(dir=REPO / "docs") as temporary:
            receipt_path = Path(temporary) / "receipt.json"
            with patch.object(
                inventory,
                "VEHICLE_ACTIVE_COMMLINK_PHONE_E2E_RECEIPT",
                receipt_path,
            ):
                self.assertIsNone(
                    inventory._validated_vehicle_active_commlink_phone_e2e_receipt(
                        presentation_root,
                        core_root,
                    )
                )
                receipt_path.write_text("{", encoding="utf-8")
                self.assertIsNone(
                    inventory._validated_vehicle_active_commlink_phone_e2e_receipt(
                        presentation_root,
                        core_root,
                    )
                )
                receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
                self.assertIsNotNone(
                    inventory._validated_vehicle_active_commlink_phone_e2e_receipt(
                        presentation_root,
                        core_root,
                    )
                )
                for key, value in (
                    ("abi", "x86_64"),
                    ("package", "invalid.package"),
                    ("apiLevel", 35),
                    ("apkSha256", "invalid"),
                    ("driverSha256", "0" * 64),
                    ("careerFixtureSha256", "0" * 64),
                    ("vehicleActiveCommlinkRulesSha256", "0" * 64),
                    ("workspaceStoreSha256", "0" * 64),
                    ("legacyMatrixAttributesSha256", "0" * 64),
                ):
                    stale = json.loads(json.dumps(receipt))
                    stale[key] = value
                    receipt_path.write_text(json.dumps(stale), encoding="utf-8")
                    self.assertIsNone(
                        inventory._validated_vehicle_active_commlink_phone_e2e_receipt(
                            presentation_root,
                            core_root,
                        ),
                        key,
                    )
                stale = json.loads(json.dumps(receipt))
                stale["controls"]["CharacterCreate.chkVehicleActiveCommlink"].pop(
                    inventory.VEHICLE_ACTIVE_COMMLINK_CONTROL_E2E_PROOF_KEYS[0]
                )
                receipt_path.write_text(json.dumps(stale), encoding="utf-8")
                self.assertIsNone(
                    inventory._validated_vehicle_active_commlink_phone_e2e_receipt(
                        presentation_root,
                        core_root,
                    )
                )

    def test_cyberware_active_commlink_phone_mapping_is_exact_phone_only_and_scripted(self) -> None:
        payload = json.loads(
            (REPO / "docs" / "ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json").read_text(
                encoding="utf-8"
            )
        )
        rows = [
            row for row in payload["rows"]
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] == inventory.CYBERWARE_ACTIVE_COMMLINK_CONTROL
        ]
        self.assertEqual(2, len(rows))
        for row in rows:
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual(
                "Build > Gear > Cyberware > selected stable persona-capable cyberware > Cyberware Active Commlink",
                row["phone"]["route"],
            )
            self.assertEqual("CyberwareActiveCommlinkPage", row["phone"]["surface"])
            self.assertEqual(
                "cyberware-active-commlink-toggle-{stable-cyberware-guid}",
                row["phone"]["automationId"],
            )
            self.assertIn("full expected Core persona semantics", row["presenterMutation"])
            self.assertIn("revision-checked atomic save", row["persistenceAssertion"])
            self.assertIn("process restart recovery", row["persistenceAssertion"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_cyberware_active_commlink_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertFalse(row["completionProven"])

    def test_cyberware_active_commlink_receipt_is_strict_full_graph_fixture_and_proof_bound(self) -> None:
        presentation_root = PRESENTATION_ROOT
        core_root = CORE_ROOT
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        native_root = REPO / "src" / "Chummer.Android" / "Native"
        driver = REPO / "tests" / "run_api36_cyberware_active_commlink_e2e.py"
        shared_driver = REPO / "tests" / "run_api36_editing_e2e.py"
        creation_fixture = REPO / "tests" / "fixtures" / "creation-cyberware-active-commlink-e2e.chum5"
        career_fixture = REPO / "tests" / "fixtures" / "career-cyberware-active-commlink-e2e.chum5"
        source_paths = {
            "cyberwareActiveCommlinkPageSha256": native_root / "CyberwareActiveCommlinkPage.cs",
            "collectionEditorPagesSha256": native_root / "CollectionEditorPages.cs",
            "coordinatorSha256": native_root / "RunnerSessionCoordinator.cs",
            "cyberwareActiveCommlinkContractSha256": overview / "CyberwareActiveCommlinkEditRequest.cs",
            "collectionEditorStateSha256": overview / "WorkspaceCollectionEditorState.cs",
            "collectionEditorProjectorSha256": overview / "WorkspaceCollectionEditorProjector.cs",
            "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
            "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
            "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
            "cyberwareActiveCommlinkRulesSha256": core_root / "Chummer.Contracts" / "Characters" / "CharacterCyberwareActiveCommlinkRules.cs",
            "weaponHomeNodeRulesSha256": core_root / "Chummer.Contracts" / "Characters" / "CharacterWeaponHomeNodeRules.cs",
            "characterSectionModelsSha256": core_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
            "characterSectionServiceSha256": core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
        }
        controls = {
            f"{form}.{inventory.CYBERWARE_ACTIVE_COMMLINK_CONTROL}": {
                key: "pass"
                for key in inventory.CYBERWARE_ACTIVE_COMMLINK_CONTROL_E2E_PROOF_KEYS
            }
            for form in ("CharacterCreate", "CharacterCareer")
        }
        receipt = {
            "schema": "chummer.android.editing-e2e/v1",
            "status": "pass",
            "profile": "phone",
            "journey": "cyberware-active-commlink",
            "apiLevel": 36,
            "abi": inventory.PHONE_E2E_ABI,
            "package": inventory.PHONE_E2E_PACKAGE,
            "apkSha256": "a" * 64,
            "driverSha256": inventory._sha256_file(driver),
            "sharedDriverSha256": inventory._sha256_file(shared_driver),
            "creationFixtureSha256": inventory._sha256_file(creation_fixture),
            "careerFixtureSha256": inventory._sha256_file(career_fixture),
            "controlCount": len(controls),
            "controls": controls,
            **{key: inventory._sha256_file(path) for key, path in source_paths.items()},
        }
        with tempfile.TemporaryDirectory(dir=REPO / "docs") as temporary:
            receipt_path = Path(temporary) / "receipt.json"
            with patch.object(
                inventory,
                "CYBERWARE_ACTIVE_COMMLINK_PHONE_E2E_RECEIPT",
                receipt_path,
            ):
                self.assertIsNone(
                    inventory._validated_cyberware_active_commlink_phone_e2e_receipt(
                        presentation_root,
                        core_root,
                    )
                )
                receipt_path.write_text("{", encoding="utf-8")
                self.assertIsNone(
                    inventory._validated_cyberware_active_commlink_phone_e2e_receipt(
                        presentation_root,
                        core_root,
                    )
                )
                receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
                self.assertIsNotNone(
                    inventory._validated_cyberware_active_commlink_phone_e2e_receipt(
                        presentation_root,
                        core_root,
                    )
                )
                for key, value in (
                    ("abi", "x86_64"),
                    ("package", "invalid.package"),
                    ("apiLevel", 35),
                    ("apkSha256", "invalid"),
                    ("driverSha256", "0" * 64),
                    ("careerFixtureSha256", "0" * 64),
                    ("cyberwareActiveCommlinkRulesSha256", "0" * 64),
                ):
                    stale = json.loads(json.dumps(receipt))
                    stale[key] = value
                    receipt_path.write_text(json.dumps(stale), encoding="utf-8")
                    self.assertIsNone(
                        inventory._validated_cyberware_active_commlink_phone_e2e_receipt(
                            presentation_root,
                            core_root,
                        ),
                        key,
                    )
                stale = json.loads(json.dumps(receipt))
                stale["controls"]["CharacterCreate.chkCyberwareActiveCommlink"].pop(
                    inventory.CYBERWARE_ACTIVE_COMMLINK_CONTROL_E2E_PROOF_KEYS[0]
                )
                receipt_path.write_text(json.dumps(stale), encoding="utf-8")
                self.assertIsNone(
                    inventory._validated_cyberware_active_commlink_phone_e2e_receipt(
                        presentation_root,
                        core_root,
                    )
                )

    def test_gear_active_commlink_phone_mapping_is_exact_phone_only_and_scripted(self) -> None:
        payload = json.loads(
            (REPO / "docs" / "ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json").read_text(
                encoding="utf-8"
            )
        )
        rows = [
            row for row in payload["rows"]
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] == inventory.GEAR_ACTIVE_COMMLINK_CONTROL
        ]
        self.assertEqual(2, len(rows))
        for row in rows:
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual(
                "Build > Gear > selected stable persona-capable gear > Gear Active Commlink",
                row["phone"]["route"],
            )
            self.assertEqual("GearActiveCommlinkPage", row["phone"]["surface"])
            self.assertEqual(
                "gear-active-commlink-toggle-{stable-gear-guid}",
                row["phone"]["automationId"],
            )
            self.assertIn("full expected Core semantics", row["presenterMutation"])
            self.assertIn("revision-checked atomic save", row["persistenceAssertion"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_gear_active_commlink_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertFalse(row["completionProven"])

    def test_career_edge_use_phone_mapping_is_exact_phone_only_and_scripted(self) -> None:
        payload = json.loads(
            (REPO / "docs" / "ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json").read_text(
                encoding="utf-8"
            )
        )
        rows = {
            row["legacy"]["controlName"]: row
            for row in payload["rows"]
            if row["legacy"]["formOrControl"] == "CharacterCareer"
            and row["legacy"]["controlName"] in {"cmdEdgeSpent", "cmdEdgeGained"}
        }
        self.assertEqual({"cmdEdgeSpent", "cmdEdgeGained"}, set(rows))
        expected_ids = {
            "cmdEdgeSpent": "career-edge-use-spend",
            "cmdEdgeGained": "career-edge-use-regain",
        }
        for control, automation_id in expected_ids.items():
            row = rows[control]
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual("Build > Runner > Edge use", row["phone"]["route"])
            self.assertEqual("CareerEdgeUsePage", row["phone"]["surface"])
            self.assertEqual(automation_id, row["phone"]["automationId"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual("tests/run_api36_career_edge_use_e2e.py", row["e2e"]["phone"]["ref"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("missing", row["e2e"]["tablet"]["status"])
            self.assertFalse(row["completionProven"])

    def test_career_manual_karma_phone_mapping_is_exact_phone_only_and_scripted(self) -> None:
        payload = json.loads(
            (REPO / "docs" / "ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json").read_text(
                encoding="utf-8"
            )
        )
        rows = {
            row["legacy"]["controlName"]: row
            for row in payload["rows"]
            if row["legacy"]["formOrControl"] == "CharacterCareer"
            and row["legacy"]["controlName"] in {"cmdKarmaGained", "cmdKarmaSpent"}
        }
        self.assertEqual({"cmdKarmaGained", "cmdKarmaSpent"}, set(rows))
        expected_ids = {
            "cmdKarmaGained": "career-manual-karma-gain",
            "cmdKarmaSpent": "career-manual-karma-spend",
        }
        for control, automation_id in expected_ids.items():
            row = rows[control]
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual("Build > Runner > Manual Karma", row["phone"]["route"])
            self.assertEqual("CareerManualKarmaPage", row["phone"]["surface"])
            self.assertEqual(automation_id, row["phone"]["automationId"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_career_manual_karma_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertIn("NuyenPerBPWftP", row["phone"]["coverageLimit"])
            self.assertIn("NuyenPerBPWftM", row["phone"]["coverageLimit"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("missing", row["e2e"]["tablet"]["status"])
            self.assertFalse(row["completionProven"])

    def test_career_manual_nuyen_phone_mapping_is_exact_phone_only_and_scripted(self) -> None:
        payload = json.loads(
            (REPO / "docs" / "ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json").read_text(
                encoding="utf-8"
            )
        )
        rows = {
            row["legacy"]["controlName"]: row
            for row in payload["rows"]
            if row["legacy"]["formOrControl"] == "CharacterCareer"
            and row["legacy"]["controlName"] in {"cmdNuyenGained", "cmdNuyenSpent"}
        }
        self.assertEqual({"cmdNuyenGained", "cmdNuyenSpent"}, set(rows))
        expected_ids = {
            "cmdNuyenGained": "career-manual-nuyen-gain",
            "cmdNuyenSpent": "career-manual-nuyen-spend",
        }
        for control, automation_id in expected_ids.items():
            row = rows[control]
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual("Build > Runner > Manual Nuyen", row["phone"]["route"])
            self.assertEqual("CareerManualNuyenPage", row["phone"]["surface"])
            self.assertEqual(automation_id, row["phone"]["automationId"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_career_manual_nuyen_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertIn("NuyenPerBPWftP", row["phone"]["coverageLimit"])
            self.assertIn("NuyenPerBPWftM", row["phone"]["coverageLimit"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("missing", row["e2e"]["tablet"]["status"])
            self.assertFalse(row["completionProven"])

    def test_career_nuyen_expense_edit_phone_mapping_covers_all_shared_handler_controls(self) -> None:
        payload = json.loads(
            (REPO / "docs" / "ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json").read_text(
                encoding="utf-8"
            )
        )
        controls = {"cmdNuyenEdit", "lstNuyen", "tsEditNuyenExpense"}
        rows = {
            row["legacy"]["controlName"]: row
            for row in payload["rows"]
            if row["legacy"]["formOrControl"] == "CharacterCareer"
            and row["legacy"]["controlName"] in controls
        }
        self.assertEqual(controls, set(rows))
        expected_ids = {
            "cmdNuyenEdit": "career-nuyen-expense-save",
            "lstNuyen": "career-nuyen-expense-picker",
            "tsEditNuyenExpense": "career-nuyen-expense-save",
        }
        for control, automation_id in expected_ids.items():
            row = rows[control]
            self.assertEqual("implemented_pending_emulator", row["phone"]["status"])
            self.assertEqual("Build > Runner > Nuyen expenses", row["phone"]["route"])
            self.assertEqual("CareerNuyenExpensePage", row["phone"]["surface"])
            self.assertEqual(automation_id, row["phone"]["automationId"])
            self.assertEqual("scripted_not_executed", row["e2e"]["phone"]["status"])
            self.assertEqual(
                "tests/run_api36_career_nuyen_expense_edit_e2e.py",
                row["e2e"]["phone"]["ref"],
            )
            self.assertIn("ManualAdd", row["phone"]["coverageLimit"])
            self.assertIn("ManualSubtract", row["phone"]["coverageLimit"])
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertEqual("missing", row["e2e"]["tablet"]["status"])
            self.assertFalse(row["completionProven"])

    def test_nuyen_expense_and_spirit_name_promote_only_with_validated_receipts(self) -> None:
        import inspect

        payload = json.loads(
            (REPO / "docs" / "ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json").read_text(
                encoding="utf-8"
            )
        )
        nuyen_row = next(
            row for row in payload["rows"]
            if row["legacy"]["formOrControl"] == "CharacterCareer"
            and row["legacy"]["controlName"] == "cmdNuyenEdit"
        )
        spirit_row = next(
            row for row in payload["rows"]
            if row["legacy"]["formOrControl"] == "SpiritControl"
            and row["legacy"]["controlName"] == "cboSpiritName"
        )
        parameters = list(inspect.signature(inventory._known_phone_mapping).parameters)
        receipt_arguments = {
            name: {} if name in {"condition_e2e_receipts", "contact_pet_e2e_receipts"} else None
            for name in parameters[4:]
        }
        executed = {
            "status": "executed_api36",
            "ref": "docs/editability-evidence/test/receipt.json",
            "receiptSha256": "a" * 64,
            "apkSha256": "b" * 64,
        }
        presentation_root = PRESENTATION_ROOT
        core_root = CORE_ROOT

        pending_nuyen = inventory._known_phone_mapping(
            nuyen_row,
            inventory.DEFAULT_CHUMMER5_ROOT,
            presentation_root,
            core_root,
            **receipt_arguments,
        )
        self.assertEqual("implemented_pending_emulator", pending_nuyen["status"])
        receipt_arguments["career_nuyen_expense_edit_phone_e2e_receipt"] = executed
        verified_nuyen = inventory._known_phone_mapping(
            nuyen_row,
            inventory.DEFAULT_CHUMMER5_ROOT,
            presentation_root,
            core_root,
            **receipt_arguments,
        )
        self.assertEqual("implemented_verified_api36", verified_nuyen["status"])
        self.assertEqual(executed, verified_nuyen["e2e"])

        receipt_arguments["career_nuyen_expense_edit_phone_e2e_receipt"] = None
        partial_spirit = inventory._known_phone_mapping(
            spirit_row,
            inventory.DEFAULT_CHUMMER5_ROOT,
            presentation_root,
            core_root,
            **receipt_arguments,
        )
        self.assertEqual("partial_exact_saved_data", partial_spirit["status"])
        self.assertEqual("missing", partial_spirit["e2e"]["status"])
        receipt_arguments["spirit_name_choice_phone_e2e_receipt"] = executed
        verified_spirit = inventory._known_phone_mapping(
            spirit_row,
            inventory.DEFAULT_CHUMMER5_ROOT,
            presentation_root,
            core_root,
            **receipt_arguments,
        )
        self.assertEqual("implemented_verified_api36", verified_spirit["status"])
        self.assertEqual(executed, verified_spirit["e2e"])

    def test_vehicle_equipment_installed_source_mapping_is_two_row_typed_and_fail_closed(self) -> None:
        import inspect

        payload = json.loads(
            (REPO / "docs" / "ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json").read_text(
                encoding="utf-8"
            )
        )
        rows = [
            row for row in payload["rows"]
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] == inventory.VEHICLE_EQUIPMENT_INSTALLED_CONTROL
        ]
        self.assertEqual(2, len(rows))
        parameters = list(inspect.signature(inventory._known_phone_mapping).parameters)
        receipt_arguments = {
            name: {} if name in {"condition_e2e_receipts", "contact_pet_e2e_receipts"} else None
            for name in parameters[4:]
        }
        for row in rows:
            mapping = inventory._known_phone_mapping(
                row,
                inventory.DEFAULT_CHUMMER5_ROOT,
                PRESENTATION_ROOT,
                CORE_ROOT,
                **receipt_arguments,
            )
            self.assertEqual("partial_exact_saved_data", mapping["status"])
            self.assertEqual(
                "Build > Gear > Vehicles > selected stable Vehicle > Installed equipment",
                mapping["route"],
            )
            self.assertEqual("VehicleEquipmentInstalledPage", mapping["surface"])
            self.assertIn("WeaponMount|VehicleMod|Weapon|WeaponAccessory", mapping["presenterMutation"])
            self.assertIn("zero Nuyen/Karma", mapping["presenterMutation"])
            self.assertIn("sensor-affecting VehicleMod", mapping["presenterMutation"])
            self.assertIn("Sensor Array rating", mapping["coverageLimit"])
            self.assertEqual("scripted_not_executed", mapping["e2e"]["status"])
            self.assertEqual(
                "tests/run_api36_vehicle_equipment_installed_e2e.py",
                mapping["e2e"]["ref"],
            )
            self.assertEqual("missing", mapping["tablet"]["status"])

    def test_select_build_method_phone_mapping_is_exact_and_fail_closed(self) -> None:
        payload = json.loads(
            (REPO / "docs" / "ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json").read_text(
                encoding="utf-8"
            )
        )
        rows = {
            row["legacy"]["controlName"]: row
            for row in payload["rows"]
            if row["legacy"]["formOrControl"] == "SelectBuildMethod"
        }

        for control in ("cboBuildMethod", "nudMaxAvail", "cboGamePlay"):
            row = rows[control]
            self.assertFalse(row["editParityRequired"])
            self.assertEqual("unreachable_designer_field", row["operation"])
            self.assertEqual("not_applicable_non_mutating", row["phone"]["status"])
            self.assertIn("never added", row["legacy"]["dispositionEvidence"])

        for control in ("cboCharacterSetting", "chkIgnoreRules", "cmdOK"):
            row = rows[control]
            self.assertTrue(row["editParityRequired"])
            verified = inventory._validated_new_character_settings_phone_e2e_receipt() is not None
            self.assertEqual(
                "implemented_verified_api36" if verified else "implemented_pending_emulator",
                row["phone"]["status"],
            )
            self.assertEqual(
                "executed_api36" if verified else "scripted_not_executed",
                row["e2e"]["phone"]["status"],
            )
            self.assertEqual("missing", row["tablet"]["status"])

        self.assertEqual("missing", rows["cmdEditCharacterSetting"]["phone"]["status"])
        self.assertEqual("not_applicable_non_mutating", rows["cmdCancel"]["phone"]["status"])

    def test_orphaned_select_setting_form_is_not_claimed_as_android_parity(self) -> None:
        payload = json.loads(
            (REPO / "docs" / "ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json").read_text(
                encoding="utf-8"
            )
        )
        rows = [
            row for row in payload["rows"]
            if row["legacy"]["formOrControl"] == "SelectSetting"
        ]

        self.assertEqual({"cboSetting", "cmdCancel", "cmdOK"}, {
            row["legacy"]["controlName"] for row in rows
        })
        for row in rows:
            self.assertEqual("unreachable_legacy_form", row["operation"])
            self.assertFalse(row["editParityRequired"])
            self.assertEqual("not_applicable_non_mutating", row["phone"]["status"])
            self.assertEqual("not_applicable_non_mutating", row["tablet"]["status"])
            self.assertIn("no reference outside", row["legacy"]["dispositionEvidence"])

    def test_character_settings_phone_mapping_is_complete_and_exactly_scoped(self) -> None:
        payload = json.loads(
            (REPO / "docs" / "ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json").read_text(
                encoding="utf-8"
            )
        )
        contract = json.loads(
            (REPO / "docs" / "CHUMMER5_CHARACTER_SETTINGS_CONTRACT.generated.json").read_text(
                encoding="utf-8"
            )
        )
        value_controls = {
            row["legacyControl"]
            for row in contract["controls"]
            if row["semanticOperation"]
            in {"set_value", "edit_sourcebooks", "edit_custom_data_directories"}
        }
        exact_controls = value_controls | set(inventory.CHARACTER_SETTINGS_ACTION_AUTOMATION_IDS)
        rows = {
            row["legacy"]["controlName"]: row
            for row in payload["rows"]
            if row["legacy"]["formOrControl"] == "EditCharacterSettings"
            and row["editParityRequired"]
        }

        self.assertEqual(162, len(rows))
        self.assertEqual(150, len(value_controls))
        self.assertEqual(exact_controls, set(rows))
        value_receipt_current = inventory._validated_character_settings_phone_e2e_receipt() is not None
        action_receipt_current = (
            inventory._validated_character_settings_actions_phone_e2e_receipt() is not None
        )
        for control in exact_controls:
            receipt_current = (
                value_receipt_current
                if control in value_controls or control in inventory.CHARACTER_SETTINGS_EXACT_API36_ACTIONS
                else action_receipt_current
            )
            self.assertEqual(
                "implemented_verified_api36" if receipt_current else "implemented_pending_emulator",
                rows[control]["phone"]["status"],
            )
            self.assertEqual(
                "executed_api36" if receipt_current else "scripted_not_executed",
                rows[control]["e2e"]["phone"]["status"],
            )
            if receipt_current and control in value_controls:
                self.assertEqual(
                    {key: "pass" for key in inventory.CHARACTER_SETTINGS_CONTROL_E2E_PROOF_KEYS},
                    rows[control]["e2e"]["phone"]["controlProof"],
                )
            if receipt_current and control in inventory.CHARACTER_SETTINGS_ACTION_E2E_CONTROLS:
                self.assertEqual(
                    {
                        key: "pass"
                        for key in inventory.CHARACTER_SETTINGS_ACTION_CONTROL_E2E_PROOF_KEYS
                    },
                    rows[control]["e2e"]["phone"]["controlProof"],
                )
        self.assertTrue(all(row["tablet"]["status"] == "missing" for row in rows.values()))
        self.assertEqual(
            "dialog-field-charactersettingsprofile",
            rows["cboSetting"]["phone"]["automationId"],
        )
        self.assertEqual(
            "dialog-action-save-and-close",
            rows["cmdOK"]["phone"]["automationId"],
        )

    def test_select_metatype_priority_phone_mapping_is_exact_and_phone_only(self) -> None:
        payload = json.loads(
            (REPO / "docs" / "ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json").read_text(
                encoding="utf-8"
            )
        )
        rows = {
            row["legacy"]["controlName"]: row
            for row in payload["rows"]
            if row["legacy"]["formOrControl"] == "SelectMetatypePriority"
        }
        proven = {
            "cboCategory",
            "lstMetatypes",
            "cboMetavariant",
            "cboHeritage",
            "cboAttributes",
            "cboTalent",
            "cboSkills",
            "cboResources",
            "cboTalents",
            "cboSkill1",
            "cboSkill2",
            "cboSkill3",
            "chkPossessionBased",
            "cboPossessionMethod",
            "nudForce",
            "cmdOK",
        }

        verified = inventory._validated_new_character_priority_phone_e2e_receipt() is not None
        for control in proven:
            row = rows[control]
            self.assertTrue(row["editParityRequired"])
            self.assertEqual(
                "implemented_verified_api36" if verified else "implemented_pending_emulator",
                row["phone"]["status"],
            )
            self.assertEqual(
                "executed_api36" if verified else "scripted_not_executed",
                row["e2e"]["phone"]["status"],
            )
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertIn("Select Metatype Priority", row["phone"]["route"])

        remaining: set[str] = set()
        self.assertEqual(
            remaining,
            {
                control
                for control, row in rows.items()
                if row["editParityRequired"] and control not in proven
            },
        )
        self.assertTrue(
            all(rows[control]["phone"]["status"] == "missing" for control in remaining)
        )

    def test_select_metatype_karma_phone_mapping_is_exact_and_phone_only(self) -> None:
        payload = json.loads(
            (REPO / "docs" / "ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json").read_text(
                encoding="utf-8"
            )
        )
        rows = {
            row["legacy"]["controlName"]: row
            for row in payload["rows"]
            if row["legacy"]["formOrControl"] == "SelectMetatypeKarma"
        }
        proven = {
            "txtSearch",
            "cboCategory",
            "lstMetatypes",
            "cboMetavariant",
            "chkPossessionBased",
            "cboPossessionMethod",
            "nudForce",
            "cmdOK",
        }

        self.assertEqual(
            proven,
            {
                control
                for control, row in rows.items()
                if row["editParityRequired"]
            },
        )
        verified = inventory._validated_new_character_karma_phone_e2e_receipt() is not None
        for control in proven:
            row = rows[control]
            self.assertEqual(
                "implemented_verified_api36" if verified else "implemented_pending_emulator",
                row["phone"]["status"],
            )
            self.assertEqual(
                "executed_api36" if verified else "scripted_not_executed",
                row["e2e"]["phone"]["status"],
            )
            self.assertEqual("missing", row["tablet"]["status"])
            self.assertIn("Select Metatype", row["phone"]["route"])
        self.assertEqual(
            "dialog-field-newcharactermetatypesearch",
            rows["txtSearch"]["phone"]["automationId"],
        )
        self.assertEqual(
            "dialog-action-complete-new-character-workflow",
            rows["cmdOK"]["phone"]["automationId"],
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

    def test_career_attribute_receipt_is_fixture_and_driver_hash_bound(self) -> None:
        validated = inventory._validated_attribute_career_phone_e2e_receipt()
        self.assertIsNotNone(validated)

        source = inventory.ATTRIBUTE_CAREER_PHONE_E2E_RECEIPT
        receipt = json.loads(source.read_text(encoding="utf-8"))
        receipt["inputFixtureSha256"] = "0" * 64
        with tempfile.TemporaryDirectory(dir=REPO / "docs") as temporary:
            receipt_path = Path(temporary) / "receipt.json"
            receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
            with patch.object(inventory, "ATTRIBUTE_CAREER_PHONE_E2E_RECEIPT", receipt_path):
                self.assertIsNone(inventory._validated_attribute_career_phone_e2e_receipt())

    def test_new_character_settings_receipt_is_source_hash_bound(self) -> None:
        self.assertIsNone(
            inventory._validated_new_character_settings_phone_e2e_receipt(),
            "The checked-in API-36 receipt must fail closed after source drift.",
        )

        source = inventory.NEW_CHARACTER_SETTINGS_PHONE_E2E_RECEIPT
        receipt = json.loads(source.read_text(encoding="utf-8"))
        native_root = REPO / "src" / "Chummer.Android" / "Native"
        overview = (
            inventory.WORKSPACE_ROOT
            / "chummer-presentation"
            / "Chummer.Presentation"
            / "Overview"
        )
        source_paths = {
            "driverSha256": REPO / "tests" / "run_api36_new_character_settings_e2e.py",
            "sharedDriverSha256": REPO / "tests" / "run_api36_editing_e2e.py",
            "nativeDialogPageSha256": native_root / "NativeDialogPage.cs",
            "buildPageSha256": native_root / "BuildPage.cs",
            "dialogFactorySha256": overview / "DesktopDialogFactory.cs",
            "dialogCoordinatorSha256": overview / "DialogCoordinator.cs",
        }
        receipt.update(
            {
                key: inventory._sha256_file(path)
                for key, path in source_paths.items()
            }
        )
        with tempfile.TemporaryDirectory(dir=REPO / "docs") as temporary:
            receipt_path = Path(temporary) / "receipt.json"
            receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
            with patch.object(inventory, "NEW_CHARACTER_SETTINGS_PHONE_E2E_RECEIPT", receipt_path):
                self.assertIsNotNone(
                    inventory._validated_new_character_settings_phone_e2e_receipt()
                )
                receipt["dialogFactorySha256"] = "0" * 64
                receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
                self.assertIsNone(inventory._validated_new_character_settings_phone_e2e_receipt())

    def test_character_settings_receipt_is_full_source_graph_hash_bound(self) -> None:
        self.assertIsNone(
            inventory._validated_character_settings_phone_e2e_receipt(),
            "The checked-in API-36 receipt must fail closed after source drift.",
        )

        source = inventory.CHARACTER_SETTINGS_PHONE_E2E_RECEIPT
        receipt = json.loads(source.read_text(encoding="utf-8"))
        native_root = REPO / "src" / "Chummer.Android" / "Native"
        overview = (
            inventory.WORKSPACE_ROOT
            / "chummer-presentation"
            / "Chummer.Presentation"
            / "Overview"
        )
        source_paths = {
            "driverSha256": REPO / "tests" / "run_api36_character_settings_e2e.py",
            "sharedDriverSha256": REPO / "tests" / "run_api36_editing_e2e.py",
            "nativeCommandPageSha256": native_root / "NativeCommandPage.cs",
            "nativeDialogPageSha256": native_root / "NativeDialogPage.cs",
            "runnerSessionCoordinatorSha256": native_root / "RunnerSessionCoordinator.cs",
            "dialogFactorySha256": overview / "DesktopDialogFactory.cs",
            "characterSettingsDialogSha256": overview / "DesktopDialogFactory.CharacterSettings.cs",
            "characterSettingsProfilesSha256": overview / "Chummer5CharacterSettingsProfiles.cs",
            "characterSettingsContractSha256": overview / "Chummer5CharacterSettingsRuntimeContract.Generated.cs",
            "dialogCoordinatorSha256": overview / "DialogCoordinator.cs",
        }
        receipt.update(
            {
                key: inventory._sha256_file(path)
                for key, path in source_paths.items()
            }
        )
        with tempfile.TemporaryDirectory(dir=REPO / "docs") as temporary:
            receipt_path = Path(temporary) / "receipt.json"
            receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
            with patch.object(inventory, "CHARACTER_SETTINGS_PHONE_E2E_RECEIPT", receipt_path):
                self.assertIsNotNone(
                    inventory._validated_character_settings_phone_e2e_receipt()
                )
                receipt["characterSettingsContractSha256"] = "0" * 64
                receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
                self.assertIsNone(inventory._validated_character_settings_phone_e2e_receipt())

    def test_character_settings_action_receipt_is_control_and_source_graph_hash_bound(self) -> None:
        self.assertIsNone(
            inventory._validated_character_settings_actions_phone_e2e_receipt(),
            "The checked-in API-36 receipt must fail closed after source drift.",
        )

        native_root = REPO / "src" / "Chummer.Android" / "Native"
        overview = (
            inventory.WORKSPACE_ROOT
            / "chummer-presentation"
            / "Chummer.Presentation"
            / "Overview"
        )
        source_paths = {
            "driverSha256": REPO / "tests" / "run_api36_character_settings_actions_e2e.py",
            "characterSettingsDriverSha256": REPO / "tests" / "run_api36_character_settings_e2e.py",
            "sharedDriverSha256": REPO / "tests" / "run_api36_editing_e2e.py",
            "nativeCommandPageSha256": native_root / "NativeCommandPage.cs",
            "nativeDialogPageSha256": native_root / "NativeDialogPage.cs",
            "runnerSessionCoordinatorSha256": native_root / "RunnerSessionCoordinator.cs",
            "dialogFactorySha256": overview / "DesktopDialogFactory.cs",
            "characterSettingsDialogSha256": overview / "DesktopDialogFactory.CharacterSettings.cs",
            "characterSettingsProfilesSha256": overview / "Chummer5CharacterSettingsProfiles.cs",
            "characterSettingsContractSha256": overview / "Chummer5CharacterSettingsRuntimeContract.Generated.cs",
            "dialogCoordinatorSha256": overview / "DialogCoordinator.cs",
        }
        controls = {
            control: {
                key: "pass"
                for key in inventory.CHARACTER_SETTINGS_ACTION_CONTROL_E2E_PROOF_KEYS
            }
            for control in inventory.CHARACTER_SETTINGS_ACTION_E2E_CONTROLS
        }
        receipt = {
            "schema": "chummer.android.editing-e2e/v1",
            "status": "pass",
            "profile": "phone",
            "journey": "character-settings-actions",
            "apiLevel": 36,
            "apkSha256": "a" * 64,
            **{
                key: inventory._sha256_file(path)
                for key, path in source_paths.items()
            },
            "controlCount": len(controls),
            "controls": controls,
            "journeys": {
                journey: "pass"
                for journey in inventory.CHARACTER_SETTINGS_ACTIONS_E2E_JOURNEYS
            },
        }
        with tempfile.TemporaryDirectory(dir=REPO / "docs") as temporary:
            receipt_path = Path(temporary) / "receipt.json"
            receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
            with patch.object(
                inventory,
                "CHARACTER_SETTINGS_ACTIONS_PHONE_E2E_RECEIPT",
                receipt_path,
            ):
                validated = inventory._validated_character_settings_actions_phone_e2e_receipt()
                self.assertIsNotNone(validated)
                assert validated is not None
                self.assertEqual(controls, validated["controlProofs"])

                for stale_hash in (
                    "driverSha256",
                    "characterSettingsProfilesSha256",
                    "dialogCoordinatorSha256",
                ):
                    stale_receipt = {**receipt, stale_hash: "0" * 64}
                    receipt_path.write_text(json.dumps(stale_receipt), encoding="utf-8")
                    self.assertIsNone(
                        inventory._validated_character_settings_actions_phone_e2e_receipt(),
                        stale_hash,
                    )

    def test_origin_dossier_receipt_is_control_and_source_graph_hash_bound(self) -> None:
        driver = REPO / "tests" / "run_api36_origin_dossier_e2e.py"
        shared_driver = REPO / "tests" / "run_api36_editing_e2e.py"
        native_root = REPO / "src" / "Chummer.Android" / "Native"
        workspace_mutations = (
            inventory.WORKSPACE_ROOT
            / "chummer-presentation"
            / "Chummer.Presentation"
            / "Overview"
            / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        )
        fixture = REPO / "tests" / "fixtures" / "career-condition-monitor-e2e.chum5"
        controls = {
            f"{form_name}.{control}": {
                key: "pass"
                for key in inventory.ORIGIN_DOSSIER_CONTROL_E2E_PROOF_KEYS
            }
            for form_name in ("CharacterCreate", "CharacterCareer")
            for control in inventory.ORIGIN_FIELDS
        }
        receipt = {
            "schema": "chummer.android.editing-e2e/v1",
            "status": "pass",
            "profile": "phone",
            "journey": "origin-dossier",
            "apiLevel": 36,
            "apkSha256": "a" * 64,
            "driverSha256": inventory._sha256_file(driver),
            "sharedDriverSha256": inventory._sha256_file(shared_driver),
            "originDossierPageSha256": inventory._sha256_file(
                native_root / "OriginDossierPage.cs"
            ),
            "runnerSessionCoordinatorSha256": inventory._sha256_file(
                native_root / "RunnerSessionCoordinator.cs"
            ),
            "workspaceMutationsSha256": inventory._sha256_file(workspace_mutations),
            "careerFixtureSha256": inventory._sha256_file(fixture),
            "controlCount": len(controls),
            "controls": controls,
            "journeys": {
                journey: "pass"
                for journey in inventory.ORIGIN_DOSSIER_E2E_JOURNEYS
            },
        }
        with tempfile.TemporaryDirectory(dir=REPO / "docs") as temporary:
            receipt_path = Path(temporary) / "receipt.json"
            receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
            with patch.object(inventory, "ORIGIN_DOSSIER_PHONE_E2E_RECEIPT", receipt_path):
                validated = inventory._validated_origin_dossier_phone_e2e_receipt()
                self.assertIsNotNone(validated)
                assert validated is not None
                self.assertEqual(controls, validated["controlProofs"])

                for stale_hash in (
                    "driverSha256",
                    "workspaceMutationsSha256",
                    "careerFixtureSha256",
                ):
                    stale_receipt = {**receipt, stale_hash: "0" * 64}
                    receipt_path.write_text(json.dumps(stale_receipt), encoding="utf-8")
                    self.assertIsNone(
                        inventory._validated_origin_dossier_phone_e2e_receipt(),
                        stale_hash,
                    )

    def test_linked_runner_receipt_is_control_and_source_graph_hash_bound(self) -> None:
        self.assertIsNone(
            inventory._validated_linked_runner_phone_e2e_receipt(),
            "The checked-in API-36 receipt must fail closed after source drift.",
        )

        native_root = REPO / "src" / "Chummer.Android"
        overview = (
            inventory.WORKSPACE_ROOT
            / "chummer-presentation"
            / "Chummer.Presentation"
            / "Overview"
        )
        fixture_root = REPO / "tests" / "fixtures"
        source_paths = {
            "driverSha256": REPO / "tests" / "run_api36_linked_runner_e2e.py",
            "sharedDriverSha256": REPO / "tests" / "run_api36_editing_e2e.py",
            "collectionEditorPagesSha256": native_root / "Native" / "CollectionEditorPages.cs",
            "runnerSessionCoordinatorSha256": native_root / "Native" / "RunnerSessionCoordinator.cs",
            "linkedCharacterFileServiceSha256": native_root / "Platform" / "IAndroidLinkedCharacterFileService.cs",
            "linkedDocumentCodecSha256": inventory.WORKSPACE_ROOT / "chummer-core-engine" / "Chummer.Infrastructure" / "Xml" / "Chummer5LinkedDocumentCodec.cs",
            "workspaceCollectionEditorProjectorSha256": overview / "WorkspaceCollectionEditorProjector.cs",
            "workspaceCollectionEditorStateSha256": overview / "WorkspaceCollectionEditorState.cs",
            "workspaceCollectionMutationRequestSha256": overview / "WorkspaceCollectionMutationRequest.cs",
            "workspaceXmlMutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
            "workspaceMutationsSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
            "inputFixtureSha256": fixture_root / "creation-contact-pet-e2e.chum5",
            "linkedFixtureSha256": fixture_root / "linked-runner-e2e.chum5",
            "invalidLinkedFixtureSha256": fixture_root / "invalid-linked-runner-e2e.chum5",
        }
        controls = {
            f"{class_name}.{control}": {
                key: "pass"
                for key in inventory.LINKED_RUNNER_CONTROL_E2E_PROOF_KEYS
            }
            for class_name in ("ContactControl", "PetControl")
            for control in ("tsAttachCharacter", "tsRemoveCharacter")
        }
        receipt = {
            "schema": "chummer.android.editing-e2e/v1",
            "status": "pass",
            "profile": "phone",
            "journey": "linked-runner",
            "apiLevel": 36,
            "apkSha256": "a" * 64,
            **{
                key: inventory._sha256_file(path)
                for key, path in source_paths.items()
            },
            "controlCount": len(controls),
            "controls": controls,
            "journeys": {
                journey: "pass"
                for journey in inventory.LINKED_RUNNER_E2E_JOURNEYS
            },
        }
        with tempfile.TemporaryDirectory(dir=REPO / "docs") as temporary:
            receipt_path = Path(temporary) / "receipt.json"
            receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
            with patch.object(inventory, "LINKED_RUNNER_PHONE_E2E_RECEIPT", receipt_path):
                validated = inventory._validated_linked_runner_phone_e2e_receipt()
                self.assertIsNotNone(validated)
                assert validated is not None
                self.assertEqual(controls, validated["controlProofs"])

                for stale_hash in (
                    "driverSha256",
                    "linkedDocumentCodecSha256",
                    "invalidLinkedFixtureSha256",
                ):
                    stale_receipt = {**receipt, stale_hash: "0" * 64}
                    receipt_path.write_text(json.dumps(stale_receipt), encoding="utf-8")
                    self.assertIsNone(
                        inventory._validated_linked_runner_phone_e2e_receipt(),
                        stale_hash,
                    )

    def test_new_character_priority_receipt_is_source_hash_bound(self) -> None:
        self.assertIsNone(
            inventory._validated_new_character_priority_phone_e2e_receipt(),
            "The checked-in API-36 receipt must fail closed after source drift.",
        )

        source = inventory.NEW_CHARACTER_PRIORITY_PHONE_E2E_RECEIPT
        receipt = json.loads(source.read_text(encoding="utf-8"))
        native_root = REPO / "src" / "Chummer.Android" / "Native"
        overview = (
            inventory.WORKSPACE_ROOT
            / "chummer-presentation"
            / "Chummer.Presentation"
            / "Overview"
        )
        source_paths = {
            "driverSha256": REPO / "tests" / "run_api36_new_character_priority_e2e.py",
            "sharedDriverSha256": REPO / "tests" / "run_api36_editing_e2e.py",
            "nativeDialogPageSha256": native_root / "NativeDialogPage.cs",
            "buildPageSha256": native_root / "BuildPage.cs",
            "dialogFactorySha256": overview / "DesktopDialogFactory.cs",
            "dialogCoordinatorSha256": overview / "DialogCoordinator.cs",
        }
        receipt.update(
            {
                key: inventory._sha256_file(path)
                for key, path in source_paths.items()
            }
        )
        with tempfile.TemporaryDirectory(dir=REPO / "docs") as temporary:
            receipt_path = Path(temporary) / "receipt.json"
            receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
            with patch.object(inventory, "NEW_CHARACTER_PRIORITY_PHONE_E2E_RECEIPT", receipt_path):
                self.assertIsNotNone(
                    inventory._validated_new_character_priority_phone_e2e_receipt()
                )
                receipt["driverSha256"] = "0" * 64
                receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
                self.assertIsNone(inventory._validated_new_character_priority_phone_e2e_receipt())

    def test_career_nuyen_expense_receipt_is_strict_full_graph_and_proof_bound(self) -> None:
        presentation_root = PRESENTATION_ROOT
        core_root = CORE_ROOT
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        driver = REPO / "tests" / "run_api36_career_nuyen_expense_edit_e2e.py"
        shared_driver = REPO / "tests" / "run_api36_editing_e2e.py"
        fixture = REPO / "tests" / "fixtures" / "career-nuyen-expense-edit-e2e.chum5"
        native_root = REPO / "src" / "Chummer.Android" / "Native"
        source_paths = {
            "careerNuyenExpensePageSha256": native_root / "CareerNuyenExpensePage.cs",
            "buildPageSha256": native_root / "BuildPage.cs",
            "coordinatorSha256": native_root / "RunnerSessionCoordinator.cs",
            "careerNuyenExpenseContractSha256": overview / "CareerNuyenExpenseEditRequest.cs",
            "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
            "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
            "presenterPersistenceSha256": overview / "CharacterOverviewPresenter.Persistence.cs",
            "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
            "careerNuyenExpenseRulesSha256": core_root / "Chummer.Contracts" / "Characters" / "CharacterCareerNuyenExpenseEditRules.cs",
            "workspaceStoreSha256": core_root / "Chummer.Infrastructure" / "Workspaces" / "FileWorkspaceStore.cs",
        }
        controls = {
            f"CharacterCareer.{control}": {
                key: "pass"
                for key in inventory.CAREER_NUYEN_EXPENSE_EDIT_CONTROL_E2E_PROOF_KEYS
            }
            for control in inventory.CAREER_NUYEN_EXPENSE_EDIT_CONTROLS
        }
        receipt = {
            "schema": "chummer.android.editing-e2e/v1",
            "status": "pass",
            "profile": "phone",
            "journey": "career-nuyen-expense-edit",
            "apiLevel": 36,
            "abi": inventory.PHONE_E2E_ABI,
            "package": inventory.PHONE_E2E_PACKAGE,
            "apkSha256": "a" * 64,
            "driverSha256": inventory._sha256_file(driver),
            "sharedDriverSha256": inventory._sha256_file(shared_driver),
            "careerFixtureSha256": inventory._sha256_file(fixture),
            "controlCount": len(controls),
            "controls": controls,
            "journeys": {
                journey: "pass"
                for journey in inventory.CAREER_NUYEN_EXPENSE_EDIT_E2E_JOURNEYS
            },
            **{key: inventory._sha256_file(path) for key, path in source_paths.items()},
        }
        with tempfile.TemporaryDirectory(dir=REPO / "docs") as temporary:
            receipt_path = Path(temporary) / "receipt.json"
            with patch.object(
                inventory,
                "CAREER_NUYEN_EXPENSE_EDIT_PHONE_E2E_RECEIPT",
                receipt_path,
            ):
                self.assertIsNone(
                    inventory._validated_career_nuyen_expense_edit_phone_e2e_receipt(
                        presentation_root,
                        core_root,
                    )
                )
                receipt_path.write_text("{", encoding="utf-8")
                self.assertIsNone(
                    inventory._validated_career_nuyen_expense_edit_phone_e2e_receipt(
                        presentation_root,
                        core_root,
                    )
                )
                receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
                self.assertIsNotNone(
                    inventory._validated_career_nuyen_expense_edit_phone_e2e_receipt(
                        presentation_root,
                        core_root,
                    )
                )
                for key, value in (
                    ("abi", "x86_64"),
                    ("package", "invalid.package"),
                    ("apiLevel", 35),
                    ("apkSha256", "invalid"),
                    ("driverSha256", "0" * 64),
                    ("careerFixtureSha256", "0" * 64),
                    ("careerNuyenExpenseRulesSha256", "0" * 64),
                ):
                    stale = json.loads(json.dumps(receipt))
                    stale[key] = value
                    receipt_path.write_text(json.dumps(stale), encoding="utf-8")
                    self.assertIsNone(
                        inventory._validated_career_nuyen_expense_edit_phone_e2e_receipt(
                            presentation_root,
                            core_root,
                        ),
                        key,
                    )
                stale = json.loads(json.dumps(receipt))
                stale["controls"]["CharacterCareer.cmdNuyenEdit"].pop(
                    inventory.CAREER_NUYEN_EXPENSE_EDIT_CONTROL_E2E_PROOF_KEYS[0]
                )
                receipt_path.write_text(json.dumps(stale), encoding="utf-8")
                self.assertIsNone(
                    inventory._validated_career_nuyen_expense_edit_phone_e2e_receipt(
                        presentation_root,
                        core_root,
                    )
                )

    def test_spirit_name_choice_receipt_is_strict_full_graph_and_proof_bound(self) -> None:
        presentation_root = PRESENTATION_ROOT
        core_root = CORE_ROOT
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        native_root = REPO / "src" / "Chummer.Android" / "Native"
        driver = REPO / "tests" / "run_api36_spirit_name_choice_e2e.py"
        shared_driver = REPO / "tests" / "run_api36_editing_e2e.py"
        creation_fixture = REPO / "tests" / "fixtures" / "creation-spirit-name-choice-e2e.chum5"
        career_fixture = REPO / "tests" / "fixtures" / "career-spirit-name-choice-e2e.chum5"
        source_paths = {
            "buildPageSha256": native_root / "BuildPage.cs",
            "buildFlowPagesSha256": native_root / "BuildFlowPages.cs",
            "spiritNameChoicePageSha256": native_root / "SpiritNameChoicePage.cs",
            "collectionEditorPagesSha256": native_root / "CollectionEditorPages.cs",
            "coordinatorSha256": native_root / "RunnerSessionCoordinator.cs",
            "spiritNameChoiceContractSha256": overview / "SpiritNameChoiceEditRequest.cs",
            "collectionEditorStateSha256": overview / "WorkspaceCollectionEditorState.cs",
            "collectionEditorProjectorSha256": overview / "WorkspaceCollectionEditorProjector.cs",
            "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
            "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
            "presenterPersistenceSha256": overview / "CharacterOverviewPresenter.Persistence.cs",
            "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
            "spiritNameChoiceRulesSha256": core_root / "Chummer.Contracts" / "Characters" / "CharacterSpiritNameChoiceRules.cs",
            "characterSectionModelsSha256": core_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
            "sourceResolverContractSha256": core_root / "Chummer.Application" / "Characters" / "ICharacterSourceDataResolver.cs",
            "sourceResolverSha256": core_root / "Chummer.Infrastructure" / "Xml" / "FileSystemCharacterSourceDataResolver.cs",
            "characterSectionServiceSha256": core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
            "traditionsCatalogSha256": core_root / "Chummer" / "data" / "traditions.xml",
            "streamsCatalogSha256": core_root / "Chummer" / "data" / "streams.xml",
            "workspaceStoreSha256": core_root / "Chummer.Infrastructure" / "Workspaces" / "FileWorkspaceStore.cs",
            "sr5ShellCatalogSha256": core_root / "Chummer.Rulesets.Sr5" / "Sr5ShellCatalogs.cs",
        }
        control = "SpiritControl.cboSpiritName"
        controls = {
            control: {
                key: "pass"
                for key in inventory.SPIRIT_NAME_CHOICE_CONTROL_E2E_PROOF_KEYS
            }
        }
        receipt = {
            "schema": "chummer.android.editing-e2e/v1",
            "status": "pass",
            "profile": "phone",
            "journey": "spirit-name-choice",
            "apiLevel": 36,
            "abi": inventory.PHONE_E2E_ABI,
            "package": inventory.PHONE_E2E_PACKAGE,
            "apkSha256": "b" * 64,
            "driverSha256": inventory._sha256_file(driver),
            "sharedDriverSha256": inventory._sha256_file(shared_driver),
            "creationFixtureSha256": inventory._sha256_file(creation_fixture),
            "careerFixtureSha256": inventory._sha256_file(career_fixture),
            "controlCount": 1,
            "controls": controls,
            "journeys": {
                journey: "pass"
                for journey in inventory.SPIRIT_NAME_CHOICE_E2E_JOURNEYS
            },
            **{key: inventory._sha256_file(path) for key, path in source_paths.items()},
        }
        with tempfile.TemporaryDirectory(dir=REPO / "docs") as temporary:
            receipt_path = Path(temporary) / "receipt.json"
            receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
            with patch.object(inventory, "SPIRIT_NAME_CHOICE_PHONE_E2E_RECEIPT", receipt_path):
                self.assertIsNotNone(
                    inventory._validated_spirit_name_choice_phone_e2e_receipt(
                        presentation_root,
                        core_root,
                    )
                )
                for key, value in (
                    ("abi", "x86_64"),
                    ("package", "invalid.package"),
                    ("status", "fail"),
                    ("apkSha256", "0"),
                    ("creationFixtureSha256", "0" * 64),
                    ("collectionEditorProjectorSha256", "0" * 64),
                ):
                    stale = json.loads(json.dumps(receipt))
                    stale[key] = value
                    receipt_path.write_text(json.dumps(stale), encoding="utf-8")
                    self.assertIsNone(
                        inventory._validated_spirit_name_choice_phone_e2e_receipt(
                            presentation_root,
                            core_root,
                        ),
                        key,
                    )
                stale = json.loads(json.dumps(receipt))
                stale["journeys"]["unexpected"] = "pass"
                receipt_path.write_text(json.dumps(stale), encoding="utf-8")
                self.assertIsNone(
                    inventory._validated_spirit_name_choice_phone_e2e_receipt(
                        presentation_root,
                        core_root,
                    )
                )

    def test_new_character_karma_receipt_is_source_hash_bound(self) -> None:
        self.assertIsNone(
            inventory._validated_new_character_karma_phone_e2e_receipt(),
            "The checked-in API-36 receipt must fail closed after source drift.",
        )

        source = inventory.NEW_CHARACTER_KARMA_PHONE_E2E_RECEIPT
        receipt = json.loads(source.read_text(encoding="utf-8"))
        native_root = REPO / "src" / "Chummer.Android" / "Native"
        overview = (
            inventory.WORKSPACE_ROOT
            / "chummer-presentation"
            / "Chummer.Presentation"
            / "Overview"
        )
        source_paths = {
            "driverSha256": REPO / "tests" / "run_api36_new_character_karma_e2e.py",
            "sharedDriverSha256": REPO / "tests" / "run_api36_editing_e2e.py",
            "helperDriverSha256": REPO / "tests" / "run_api36_new_character_priority_e2e.py",
            "nativeDialogPageSha256": native_root / "NativeDialogPage.cs",
            "buildPageSha256": native_root / "BuildPage.cs",
            "dialogFactorySha256": overview / "DesktopDialogFactory.cs",
            "dialogCoordinatorSha256": overview / "DialogCoordinator.cs",
        }
        receipt.update(
            {
                key: inventory._sha256_file(path)
                for key, path in source_paths.items()
            }
        )
        with tempfile.TemporaryDirectory(dir=REPO / "docs") as temporary:
            receipt_path = Path(temporary) / "receipt.json"
            receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
            with patch.object(inventory, "NEW_CHARACTER_KARMA_PHONE_E2E_RECEIPT", receipt_path):
                self.assertIsNotNone(
                    inventory._validated_new_character_karma_phone_e2e_receipt()
                )
                receipt["dialogFactorySha256"] = "0" * 64
                receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
                self.assertIsNone(inventory._validated_new_character_karma_phone_e2e_receipt())

    def test_armor_home_node_receipt_is_full_source_graph_and_fixture_hash_bound(self) -> None:
        presentation_root = PRESENTATION_ROOT
        core_root = CORE_ROOT
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        driver = REPO / "tests" / "run_api36_armor_home_node_e2e.py"
        shared_driver = REPO / "tests" / "run_api36_editing_e2e.py"
        creation_fixture = REPO / "tests" / "fixtures" / "creation-armor-home-node-e2e.chum5"
        career_fixture = REPO / "tests" / "fixtures" / "career-armor-home-node-e2e.chum5"
        source_paths = {
            "armorHomeNodePageSha256": REPO / "src" / "Chummer.Android" / "Native" / "ArmorHomeNodePage.cs",
            "collectionEditorPagesSha256": REPO / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
            "coordinatorSha256": REPO / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
            "armorHomeNodeContractSha256": overview / "ArmorHomeNodeEditRequest.cs",
            "collectionEditorStateSha256": overview / "WorkspaceCollectionEditorState.cs",
            "collectionEditorProjectorSha256": overview / "WorkspaceCollectionEditorProjector.cs",
            "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
            "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
            "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
            "characterSectionModelsSha256": core_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
            "characterSectionServiceSha256": core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
        }
        controls = {
            f"{form}.{inventory.ARMOR_HOME_NODE_CONTROL}": {
                key: "pass" for key in inventory.ARMOR_HOME_NODE_CONTROL_E2E_PROOF_KEYS
            }
            for form in ("CharacterCreate", "CharacterCareer")
        }
        receipt = {
            "schema": "chummer.android.editing-e2e/v1",
            "status": "pass",
            "profile": "phone",
            "journey": "armor-home-node",
            "apiLevel": 36,
            "apkSha256": "a" * 64,
            "driverSha256": inventory._sha256_file(driver),
            "sharedDriverSha256": inventory._sha256_file(shared_driver),
            "creationFixtureSha256": inventory._sha256_file(creation_fixture),
            "careerFixtureSha256": inventory._sha256_file(career_fixture),
            "controlCount": len(controls),
            "controls": controls,
            "journeys": {key: "pass" for key in inventory.ARMOR_HOME_NODE_E2E_JOURNEYS},
            **{key: inventory._sha256_file(path) for key, path in source_paths.items()},
        }
        with tempfile.TemporaryDirectory(dir=REPO / "docs") as temporary:
            receipt_path = Path(temporary) / "receipt.json"
            receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
            with patch.object(inventory, "ARMOR_HOME_NODE_PHONE_E2E_RECEIPT", receipt_path):
                self.assertIsNotNone(
                    inventory._validated_armor_home_node_phone_e2e_receipt(
                        presentation_root,
                        core_root,
                    )
                )
                receipt["characterSectionModelsSha256"] = "0" * 64
                receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
                self.assertIsNone(
                    inventory._validated_armor_home_node_phone_e2e_receipt(
                        presentation_root,
                        core_root,
                    )
                )

    def test_weapon_home_node_receipt_is_full_source_graph_and_fixture_hash_bound(self) -> None:
        presentation_root = PRESENTATION_ROOT
        core_root = CORE_ROOT
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        driver = REPO / "tests" / "run_api36_weapon_home_node_e2e.py"
        shared_driver = REPO / "tests" / "run_api36_editing_e2e.py"
        creation_fixture = REPO / "tests" / "fixtures" / "creation-weapon-home-node-e2e.chum5"
        career_fixture = REPO / "tests" / "fixtures" / "career-weapon-home-node-e2e.chum5"
        source_paths = {
            "weaponHomeNodePageSha256": REPO / "src" / "Chummer.Android" / "Native" / "WeaponHomeNodePage.cs",
            "collectionEditorPagesSha256": REPO / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
            "coordinatorSha256": REPO / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
            "weaponHomeNodeContractSha256": overview / "WeaponHomeNodeEditRequest.cs",
            "collectionEditorStateSha256": overview / "WorkspaceCollectionEditorState.cs",
            "collectionEditorProjectorSha256": overview / "WorkspaceCollectionEditorProjector.cs",
            "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
            "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
            "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
            "weaponHomeNodeRulesSha256": core_root / "Chummer.Contracts" / "Characters" / "CharacterWeaponHomeNodeRules.cs",
            "weaponParentResolverSha256": core_root / "Chummer.Contracts" / "Characters" / "CharacterWeaponMatrixParentResolver.cs",
            "characterSectionModelsSha256": core_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
            "characterSectionServiceSha256": core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
        }
        controls = {
            f"{form}.{inventory.WEAPON_HOME_NODE_CONTROL}": {
                key: "pass" for key in inventory.WEAPON_HOME_NODE_CONTROL_E2E_PROOF_KEYS
            }
            for form in ("CharacterCreate", "CharacterCareer")
        }
        receipt = {
            "schema": "chummer.android.editing-e2e/v1",
            "status": "pass",
            "profile": "phone",
            "journey": "weapon-home-node",
            "apiLevel": 36,
            "apkSha256": "a" * 64,
            "driverSha256": inventory._sha256_file(driver),
            "sharedDriverSha256": inventory._sha256_file(shared_driver),
            "creationFixtureSha256": inventory._sha256_file(creation_fixture),
            "careerFixtureSha256": inventory._sha256_file(career_fixture),
            "controlCount": len(controls),
            "controls": controls,
            "journeys": {key: "pass" for key in inventory.WEAPON_HOME_NODE_E2E_JOURNEYS},
            **{key: inventory._sha256_file(path) for key, path in source_paths.items()},
        }
        with tempfile.TemporaryDirectory(dir=REPO / "docs") as temporary:
            receipt_path = Path(temporary) / "receipt.json"
            receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
            with patch.object(inventory, "WEAPON_HOME_NODE_PHONE_E2E_RECEIPT", receipt_path):
                self.assertIsNotNone(
                    inventory._validated_weapon_home_node_phone_e2e_receipt(
                        presentation_root,
                        core_root,
                    )
                )
                receipt["weaponHomeNodeRulesSha256"] = "0" * 64
                receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
                self.assertIsNone(
                    inventory._validated_weapon_home_node_phone_e2e_receipt(
                        presentation_root,
                        core_root,
                    )
                )

    def test_armor_active_commlink_receipt_is_full_source_graph_and_fixture_hash_bound(self) -> None:
        presentation_root = PRESENTATION_ROOT
        core_root = CORE_ROOT
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        driver = REPO / "tests" / "run_api36_armor_active_commlink_e2e.py"
        shared_driver = REPO / "tests" / "run_api36_editing_e2e.py"
        creation_fixture = REPO / "tests" / "fixtures" / "creation-armor-active-commlink-e2e.chum5"
        career_fixture = REPO / "tests" / "fixtures" / "career-armor-active-commlink-e2e.chum5"
        source_paths = {
            "armorActiveCommlinkPageSha256": REPO / "src" / "Chummer.Android" / "Native" / "ArmorActiveCommlinkPage.cs",
            "collectionEditorPagesSha256": REPO / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
            "coordinatorSha256": REPO / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
            "armorActiveCommlinkContractSha256": overview / "ArmorActiveCommlinkEditRequest.cs",
            "collectionEditorStateSha256": overview / "WorkspaceCollectionEditorState.cs",
            "collectionEditorProjectorSha256": overview / "WorkspaceCollectionEditorProjector.cs",
            "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
            "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
            "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
            "characterSectionModelsSha256": core_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
            "characterSectionServiceSha256": core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
        }
        controls = {
            f"{form}.{inventory.ARMOR_ACTIVE_COMMLINK_CONTROL}": {
                key: "pass" for key in inventory.ARMOR_ACTIVE_COMMLINK_CONTROL_E2E_PROOF_KEYS
            }
            for form in ("CharacterCreate", "CharacterCareer")
        }
        receipt = {
            "schema": "chummer.android.editing-e2e/v1",
            "status": "pass",
            "profile": "phone",
            "journey": "armor-active-commlink",
            "apiLevel": 36,
            "apkSha256": "a" * 64,
            "driverSha256": inventory._sha256_file(driver),
            "sharedDriverSha256": inventory._sha256_file(shared_driver),
            "creationFixtureSha256": inventory._sha256_file(creation_fixture),
            "careerFixtureSha256": inventory._sha256_file(career_fixture),
            "controlCount": len(controls),
            "controls": controls,
            "journeys": {key: "pass" for key in inventory.ARMOR_ACTIVE_COMMLINK_E2E_JOURNEYS},
            **{key: inventory._sha256_file(path) for key, path in source_paths.items()},
        }
        with tempfile.TemporaryDirectory(dir=REPO / "docs") as temporary:
            receipt_path = Path(temporary) / "receipt.json"
            receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
            with patch.object(inventory, "ARMOR_ACTIVE_COMMLINK_PHONE_E2E_RECEIPT", receipt_path):
                self.assertIsNotNone(
                    inventory._validated_armor_active_commlink_phone_e2e_receipt(
                        presentation_root,
                        core_root,
                    )
                )
                receipt["characterSectionServiceSha256"] = "0" * 64
                receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
                self.assertIsNone(
                    inventory._validated_armor_active_commlink_phone_e2e_receipt(
                        presentation_root,
                        core_root,
                    )
                )

    def test_weapon_active_commlink_receipt_is_full_source_graph_and_fixture_hash_bound(self) -> None:
        presentation_root = PRESENTATION_ROOT
        core_root = CORE_ROOT
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        driver = REPO / "tests" / "run_api36_weapon_active_commlink_e2e.py"
        shared_driver = REPO / "tests" / "run_api36_editing_e2e.py"
        creation_fixture = REPO / "tests" / "fixtures" / "creation-weapon-active-commlink-e2e.chum5"
        career_fixture = REPO / "tests" / "fixtures" / "career-weapon-active-commlink-e2e.chum5"
        source_paths = {
            "weaponActiveCommlinkPageSha256": REPO / "src" / "Chummer.Android" / "Native" / "WeaponActiveCommlinkPage.cs",
            "collectionEditorPagesSha256": REPO / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
            "coordinatorSha256": REPO / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
            "weaponActiveCommlinkContractSha256": overview / "WeaponActiveCommlinkEditRequest.cs",
            "collectionEditorStateSha256": overview / "WorkspaceCollectionEditorState.cs",
            "collectionEditorProjectorSha256": overview / "WorkspaceCollectionEditorProjector.cs",
            "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
            "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
            "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
            "weaponActiveCommlinkRulesSha256": core_root / "Chummer.Contracts" / "Characters" / "CharacterWeaponActiveCommlinkRules.cs",
            "weaponHomeNodeRulesSha256": core_root / "Chummer.Contracts" / "Characters" / "CharacterWeaponHomeNodeRules.cs",
            "weaponParentResolverSha256": core_root / "Chummer.Contracts" / "Characters" / "CharacterWeaponMatrixParentResolver.cs",
            "characterSectionModelsSha256": core_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
            "characterSectionServiceSha256": core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
        }
        controls = {
            f"{form}.{inventory.WEAPON_ACTIVE_COMMLINK_CONTROL}": {
                key: "pass" for key in inventory.WEAPON_ACTIVE_COMMLINK_CONTROL_E2E_PROOF_KEYS
            }
            for form in ("CharacterCreate", "CharacterCareer")
        }
        receipt = {
            "schema": "chummer.android.editing-e2e/v1",
            "status": "pass",
            "profile": "phone",
            "journey": "weapon-active-commlink",
            "apiLevel": 36,
            "apkSha256": "a" * 64,
            "driverSha256": inventory._sha256_file(driver),
            "sharedDriverSha256": inventory._sha256_file(shared_driver),
            "creationFixtureSha256": inventory._sha256_file(creation_fixture),
            "careerFixtureSha256": inventory._sha256_file(career_fixture),
            "controlCount": len(controls),
            "controls": controls,
            "journeys": {
                key: "pass" for key in inventory.WEAPON_ACTIVE_COMMLINK_E2E_JOURNEYS
            },
            **{key: inventory._sha256_file(path) for key, path in source_paths.items()},
        }
        with tempfile.TemporaryDirectory(dir=REPO / "docs") as temporary:
            receipt_path = Path(temporary) / "receipt.json"
            receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
            with patch.object(inventory, "WEAPON_ACTIVE_COMMLINK_PHONE_E2E_RECEIPT", receipt_path):
                self.assertIsNotNone(
                    inventory._validated_weapon_active_commlink_phone_e2e_receipt(
                        presentation_root,
                        core_root,
                    )
                )
                receipt["weaponActiveCommlinkRulesSha256"] = "0" * 64
                receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
                self.assertIsNone(
                    inventory._validated_weapon_active_commlink_phone_e2e_receipt(
                        presentation_root,
                        core_root,
                    )
                )

    def test_armor_damage_receipt_is_full_source_graph_and_fixture_hash_bound(self) -> None:
        presentation_root = PRESENTATION_ROOT
        core_root = CORE_ROOT
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        driver = REPO / "tests" / "run_api36_armor_damage_e2e.py"
        shared_driver = REPO / "tests" / "run_api36_editing_e2e.py"
        career_fixture = REPO / "tests" / "fixtures" / "career-armor-damage-e2e.chum5"
        source_paths = {
            "armorDamagePageSha256": REPO / "src" / "Chummer.Android" / "Native" / "ArmorDamagePage.cs",
            "collectionEditorPagesSha256": REPO / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
            "coordinatorSha256": REPO / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
            "armorDamageContractSha256": overview / "ArmorDamageAdjustmentRequest.cs",
            "collectionEditorStateSha256": overview / "WorkspaceCollectionEditorState.cs",
            "collectionEditorProjectorSha256": overview / "WorkspaceCollectionEditorProjector.cs",
            "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
            "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
            "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
            "armorDamageRulesSha256": core_root / "Chummer.Contracts" / "Characters" / "CharacterArmorDamageRules.cs",
            "characterSectionModelsSha256": core_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
            "characterSectionServiceSha256": core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
        }
        controls = {
            f"CharacterCareer.{control}": {
                key: "pass" for key in inventory.ARMOR_DAMAGE_CONTROL_E2E_PROOF_KEYS
            }
            for control in inventory.ARMOR_DAMAGE_CONTROLS
        }
        receipt = {
            "schema": "chummer.android.editing-e2e/v1",
            "status": "pass",
            "profile": "phone",
            "journey": "armor-damage",
            "apiLevel": 36,
            "apkSha256": "a" * 64,
            "driverSha256": inventory._sha256_file(driver),
            "sharedDriverSha256": inventory._sha256_file(shared_driver),
            "careerFixtureSha256": inventory._sha256_file(career_fixture),
            "controlCount": len(controls),
            "controls": controls,
            "journeys": {key: "pass" for key in inventory.ARMOR_DAMAGE_E2E_JOURNEYS},
            **{key: inventory._sha256_file(path) for key, path in source_paths.items()},
        }
        with tempfile.TemporaryDirectory(dir=REPO / "docs") as temporary:
            receipt_path = Path(temporary) / "receipt.json"
            receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
            with patch.object(inventory, "ARMOR_DAMAGE_PHONE_E2E_RECEIPT", receipt_path):
                self.assertIsNotNone(
                    inventory._validated_armor_damage_phone_e2e_receipt(
                        presentation_root,
                        core_root,
                    )
                )
                receipt["armorDamageRulesSha256"] = "0" * 64
                receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
                self.assertIsNone(
                    inventory._validated_armor_damage_phone_e2e_receipt(
                        presentation_root,
                        core_root,
                    )
                )

    def test_armor_equipment_receipt_is_full_source_graph_and_fixture_hash_bound(self) -> None:
        presentation_root = PRESENTATION_ROOT
        core_root = CORE_ROOT
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        contracts = core_root / "Chummer.Contracts" / "Characters"
        driver = REPO / "tests" / "run_api36_armor_equipment_e2e.py"
        shared_driver = REPO / "tests" / "run_api36_editing_e2e.py"
        creation_fixture = REPO / "tests" / "fixtures" / "creation-armor-equipment-e2e.chum5"
        career_fixture = REPO / "tests" / "fixtures" / "career-armor-equipment-e2e.chum5"
        source_paths = {
            "armorEquipmentPageSha256": REPO / "src" / "Chummer.Android" / "Native" / "ArmorEquipmentPage.cs",
            "collectionEditorPagesSha256": REPO / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
            "coordinatorSha256": REPO / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
            "armorEquipmentContractSha256": overview / "ArmorEquipmentEditRequest.cs",
            "collectionEditorStateSha256": overview / "WorkspaceCollectionEditorState.cs",
            "collectionEditorProjectorSha256": overview / "WorkspaceCollectionEditorProjector.cs",
            "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
            "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
            "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
            "armorEquipmentRulesSha256": contracts / "CharacterArmorEquipmentRules.cs",
            "characterSectionModelsSha256": contracts / "CharacterSectionModels.cs",
            "characterSectionServiceSha256": core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
        }
        controls = {
            f"{form}.{control}": {
                key: "pass" for key in inventory.ARMOR_EQUIPMENT_CONTROL_E2E_PROOF_KEYS
            }
            for form in ("CharacterCreate", "CharacterCareer")
            for control in inventory.ARMOR_EQUIPMENT_CONTROLS
        }
        receipt = {
            "schema": "chummer.android.editing-e2e/v1",
            "status": "pass",
            "profile": "phone",
            "journey": "armor-equipment",
            "apiLevel": 36,
            "apkSha256": "a" * 64,
            "driverSha256": inventory._sha256_file(driver),
            "sharedDriverSha256": inventory._sha256_file(shared_driver),
            "creationFixtureSha256": inventory._sha256_file(creation_fixture),
            "careerFixtureSha256": inventory._sha256_file(career_fixture),
            "controlCount": len(controls),
            "controls": controls,
            "journeys": {key: "pass" for key in inventory.ARMOR_EQUIPMENT_E2E_JOURNEYS},
            **{key: inventory._sha256_file(path) for key, path in source_paths.items()},
        }
        with tempfile.TemporaryDirectory(dir=REPO / "docs") as temporary:
            receipt_path = Path(temporary) / "receipt.json"
            receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
            with patch.object(inventory, "ARMOR_EQUIPMENT_PHONE_E2E_RECEIPT", receipt_path):
                self.assertIsNotNone(
                    inventory._validated_armor_equipment_phone_e2e_receipt(
                        presentation_root,
                        core_root,
                    )
                )
                receipt["armorEquipmentRulesSha256"] = "0" * 64
                receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
                self.assertIsNone(
                    inventory._validated_armor_equipment_phone_e2e_receipt(
                        presentation_root,
                        core_root,
                    )
                )

    def test_weapon_accessory_included_receipt_is_full_source_graph_and_fixture_hash_bound(self) -> None:
        presentation_root = PRESENTATION_ROOT
        core_root = CORE_ROOT
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        driver = REPO / "tests" / "run_api36_weapon_accessory_included_e2e.py"
        shared_driver = REPO / "tests" / "run_api36_editing_e2e.py"
        creation_fixture = REPO / "tests" / "fixtures" / "creation-weapon-accessory-included-e2e.chum5"
        career_fixture = REPO / "tests" / "fixtures" / "career-weapon-accessory-included-e2e.chum5"
        source_paths = {
            "weaponAccessoryIncludedPageSha256": REPO / "src" / "Chummer.Android" / "Native" / "WeaponAccessoryIncludedPage.cs",
            "collectionEditorPagesSha256": REPO / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
            "coordinatorSha256": REPO / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
            "weaponAccessoryIncludedContractSha256": overview / "WeaponAccessoryIncludedEditRequest.cs",
            "collectionEditorStateSha256": overview / "WorkspaceCollectionEditorState.cs",
            "collectionEditorProjectorSha256": overview / "WorkspaceCollectionEditorProjector.cs",
            "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
            "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
            "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
            "characterSectionModelsSha256": core_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
            "characterSectionServiceSha256": core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
        }
        controls = {
            f"{form}.{inventory.WEAPON_ACCESSORY_INCLUDED_CONTROL}": {
                key: "pass" for key in inventory.WEAPON_ACCESSORY_INCLUDED_CONTROL_E2E_PROOF_KEYS
            }
            for form in ("CharacterCreate", "CharacterCareer")
        }
        receipt = {
            "schema": "chummer.android.editing-e2e/v1",
            "status": "pass",
            "profile": "phone",
            "journey": "weapon-accessory-included",
            "apiLevel": 36,
            "apkSha256": "a" * 64,
            "driverSha256": inventory._sha256_file(driver),
            "sharedDriverSha256": inventory._sha256_file(shared_driver),
            "creationFixtureSha256": inventory._sha256_file(creation_fixture),
            "careerFixtureSha256": inventory._sha256_file(career_fixture),
            "controlCount": len(controls),
            "controls": controls,
            "journeys": {key: "pass" for key in inventory.WEAPON_ACCESSORY_INCLUDED_E2E_JOURNEYS},
            **{key: inventory._sha256_file(path) for key, path in source_paths.items()},
        }
        with tempfile.TemporaryDirectory(dir=REPO / "docs") as temporary:
            receipt_path = Path(temporary) / "receipt.json"
            receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
            with patch.object(inventory, "WEAPON_ACCESSORY_INCLUDED_PHONE_E2E_RECEIPT", receipt_path):
                self.assertIsNotNone(
                    inventory._validated_weapon_accessory_included_phone_e2e_receipt(
                        presentation_root,
                        core_root,
                    )
                )
                receipt["mutationCatalogSha256"] = "0" * 64
                receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
                self.assertIsNone(
                    inventory._validated_weapon_accessory_included_phone_e2e_receipt(
                        presentation_root,
                        core_root,
                    )
                )

    def test_critter_power_count_receipt_is_full_source_graph_and_fixture_hash_bound(self) -> None:
        presentation_root = PRESENTATION_ROOT
        core_root = CORE_ROOT
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        driver = REPO / "tests" / "run_api36_critter_power_count_e2e.py"
        shared_driver = REPO / "tests" / "run_api36_editing_e2e.py"
        creation_fixture = REPO / "tests" / "fixtures" / "creation-critter-power-count-e2e.chum5"
        career_fixture = REPO / "tests" / "fixtures" / "career-critter-power-count-e2e.chum5"
        source_paths = {
            "critterPowerCountPageSha256": REPO / "src" / "Chummer.Android" / "Native" / "CritterPowerCountPage.cs",
            "collectionEditorPagesSha256": REPO / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
            "coordinatorSha256": REPO / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
            "critterPowerCountContractSha256": overview / "CritterPowerCountEditRequest.cs",
            "collectionEditorStateSha256": overview / "WorkspaceCollectionEditorState.cs",
            "collectionEditorProjectorSha256": overview / "WorkspaceCollectionEditorProjector.cs",
            "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
            "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
            "presenterPersistenceSha256": overview / "CharacterOverviewPresenter.Persistence.cs",
            "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
            "critterPowerCountRulesSha256": core_root / "Chummer.Contracts" / "Characters" / "CharacterCritterPowerCountRules.cs",
            "characterSectionModelsSha256": core_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
            "characterSectionServiceSha256": core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
            "workspaceStoreSha256": core_root / "Chummer.Infrastructure" / "Workspaces" / "FileWorkspaceStore.cs",
        }
        controls = {
            f"{form}.{inventory.CRITTER_POWER_COUNT_CONTROL}": {
                key: "pass" for key in inventory.CRITTER_POWER_COUNT_CONTROL_E2E_PROOF_KEYS
            }
            for form in ("CharacterCreate", "CharacterCareer")
        }
        receipt = {
            "schema": "chummer.android.editing-e2e/v1",
            "status": "pass",
            "profile": "phone",
            "journey": "critter-power-count",
            "apiLevel": 36,
            "apkSha256": "a" * 64,
            "driverSha256": inventory._sha256_file(driver),
            "sharedDriverSha256": inventory._sha256_file(shared_driver),
            "creationFixtureSha256": inventory._sha256_file(creation_fixture),
            "careerFixtureSha256": inventory._sha256_file(career_fixture),
            "controlCount": len(controls),
            "controls": controls,
            "journeys": {key: "pass" for key in inventory.CRITTER_POWER_COUNT_E2E_JOURNEYS},
            **{key: inventory._sha256_file(path) for key, path in source_paths.items()},
        }
        with tempfile.TemporaryDirectory(dir=REPO / "docs") as temporary:
            receipt_path = Path(temporary) / "receipt.json"
            receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
            with patch.object(inventory, "CRITTER_POWER_COUNT_PHONE_E2E_RECEIPT", receipt_path):
                self.assertIsNotNone(
                    inventory._validated_critter_power_count_phone_e2e_receipt(
                        presentation_root,
                        core_root,
                    )
                )
                receipt["critterPowerCountRulesSha256"] = "0" * 64
                receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
                self.assertIsNone(
                    inventory._validated_critter_power_count_phone_e2e_receipt(
                        presentation_root,
                        core_root,
                    )
                )

    def test_sustained_effects_receipt_is_full_source_graph_and_fixture_hash_bound(self) -> None:
        presentation_root = PRESENTATION_ROOT
        core_root = CORE_ROOT
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        driver = REPO / "tests" / "run_api36_sustained_effects_e2e.py"
        shared_driver = REPO / "tests" / "run_api36_editing_e2e.py"
        creation_fixture = REPO / "tests" / "fixtures" / "creation-sustained-effects-e2e.chum5"
        career_fixture = REPO / "tests" / "fixtures" / "career-sustained-effects-e2e.chum5"
        source_paths = {
            "buildPageSha256": REPO / "src" / "Chummer.Android" / "Native" / "BuildPage.cs",
            "sustainedEffectsPageSha256": REPO / "src" / "Chummer.Android" / "Native" / "SustainedObjectsPage.cs",
            "coordinatorSha256": REPO / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
            "sustainedEffectsContractSha256": overview / "SustainedObjectEditRequest.cs",
            "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
            "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
            "presenterPersistenceSha256": overview / "CharacterOverviewPresenter.Persistence.cs",
            "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
            "sustainedEffectsRulesSha256": core_root / "Chummer.Contracts" / "Characters" / "CharacterSustainedObjectRules.cs",
            "workspaceStoreSha256": core_root / "Chummer.Infrastructure" / "Workspaces" / "FileWorkspaceStore.cs",
        }
        controls = {
            f"SustainedObjectControl.{control}": {
                key: "pass" for key in inventory.SUSTAINED_EFFECTS_CONTROL_E2E_PROOF_KEYS
            }
            for control in inventory.SUSTAINED_EFFECTS_CONTROLS
        }
        receipt = {
            "schema": "chummer.android.editing-e2e/v1",
            "status": "pass",
            "profile": "phone",
            "journey": "sustained-effects",
            "apiLevel": 36,
            "apkSha256": "a" * 64,
            "driverSha256": inventory._sha256_file(driver),
            "sharedDriverSha256": inventory._sha256_file(shared_driver),
            "creationFixtureSha256": inventory._sha256_file(creation_fixture),
            "careerFixtureSha256": inventory._sha256_file(career_fixture),
            "controlCount": len(controls),
            "controls": controls,
            "journeys": {key: "pass" for key in inventory.SUSTAINED_EFFECTS_E2E_JOURNEYS},
            **{key: inventory._sha256_file(path) for key, path in source_paths.items()},
        }
        with tempfile.TemporaryDirectory(dir=REPO / "docs") as temporary:
            receipt_path = Path(temporary) / "receipt.json"
            receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
            with patch.object(inventory, "SUSTAINED_EFFECTS_PHONE_E2E_RECEIPT", receipt_path):
                self.assertIsNotNone(
                    inventory._validated_sustained_effects_phone_e2e_receipt(
                        presentation_root,
                        core_root,
                    )
                )
                receipt["sustainedEffectsRulesSha256"] = "0" * 64
                receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
                self.assertIsNone(
                    inventory._validated_sustained_effects_phone_e2e_receipt(
                        presentation_root,
                        core_root,
                    )
                )

    def test_vehicle_matrix_source_mapping_is_eight_row_partial(self) -> None:
        source = SCRIPT.read_text(encoding="utf-8")
        self.assertEqual({
            "cboVehicleAttack": "cboVehicleAttack_SelectedIndexChanged",
            "cboVehicleSleaze": "cboVehicleSleaze_SelectedIndexChanged",
            "cboVehicleDataProcessing": "cboVehicleDataProcessing_SelectedIndexChanged",
            "cboVehicleFirewall": "cboVehicleFirewall_SelectedIndexChanged",
        }, inventory.VEHICLE_MATRIX_SWAP_CONTROLS)
        for marker in (
            "VehicleDataProcessingFirewallSwapPage",
            "VehicleDataProcessingFirewallSwapEditRequest.cs",
            "CharacterVehicleMatrixSwapRules.cs",
            '"status": "partial_exact_saved_data" if implemented else "missing"',
            "root Vehicle selection only",
            "descendant Weapon/Cyberware/Gear tree and clip-ammo parent paths",
            "tests/run_api36_vehicle_dp_firewall_swap_e2e.py",
        ):
            self.assertIn(marker, source)

    def test_cyberware_matrix_source_mapping_is_eight_row_typed_partial_and_scripted(self) -> None:
        import inspect

        source = SCRIPT.read_text(encoding="utf-8")
        self.assertEqual({
            "cboCyberwareAttack": "cboCyberwareAttack_SelectedIndexChanged",
            "cboCyberwareSleaze": "cboCyberwareSleaze_SelectedIndexChanged",
            "cboCyberwareDataProcessing": "cboCyberwareDataProcessing_SelectedIndexChanged",
            "cboCyberwareFirewall": "cboCyberwareFirewall_SelectedIndexChanged",
        }, inventory.CYBERWARE_MATRIX_SWAP_CONTROLS)
        payload = json.loads(
            (REPO / "docs" / "ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json").read_text(
                encoding="utf-8"
            )
        )
        rows = [
            row for row in payload["rows"]
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] in inventory.CYBERWARE_MATRIX_SWAP_CONTROLS
        ]
        self.assertEqual(8, len(rows))
        parameters = list(inspect.signature(inventory._known_phone_mapping).parameters)
        receipt_arguments = {
            name: {} if name in {"condition_e2e_receipts", "contact_pet_e2e_receipts"} else None
            for name in parameters[4:]
        }
        for row in rows:
            mapping = inventory._known_phone_mapping(
                row,
                inventory.DEFAULT_CHUMMER5_ROOT,
                PRESENTATION_ROOT,
                CORE_ROOT,
                **receipt_arguments,
            )
            self.assertEqual("partial_exact_saved_data", mapping["status"])
            self.assertEqual("CyberwareMatrixSwapPage", mapping["surface"])
            self.assertEqual(
                "cyberware-matrix-swap-changed-{stable-cyberware-guid}",
                mapping["automationId"],
            )
            self.assertIn("typed root Cyberware", mapping["presenterMutation"])
            self.assertIn("zero Nuyen/Karma", mapping["presenterMutation"])
            self.assertIn("descendant Cyberware and child Gear", mapping["coverageLimit"])
            self.assertEqual("scripted_not_executed", mapping["e2e"]["status"])
            self.assertEqual(
                "tests/run_api36_cyberware_matrix_swap_e2e.py",
                mapping["e2e"]["ref"],
            )
            self.assertEqual("missing", mapping["tablet"]["status"])
        for marker in (
            "CyberwareMatrixSwapPage",
            "CyberwareMatrixSwapEditRequest.cs",
            "CharacterCyberwareMatrixSwapRules.cs",
            '"status": "partial_exact_saved_data" if implemented and legacy_exact else "missing"',
            "top-level Cyberware root selection only",
            "tests/run_api36_cyberware_matrix_swap_e2e.py",
        ):
            self.assertIn(marker, source)

    def test_vehicle_weapon_firing_mode_mapping_is_two_row_typed_partial_and_scripted(self) -> None:
        import inspect

        source = SCRIPT.read_text(encoding="utf-8")
        self.assertEqual(
            "cboVehicleWeaponFiringMode",
            inventory.VEHICLE_WEAPON_FIRING_MODE_CONTROL,
        )
        payload = json.loads(
            (REPO / "docs" / "ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json").read_text(
                encoding="utf-8"
            )
        )
        rows = [
            row for row in payload["rows"]
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] == inventory.VEHICLE_WEAPON_FIRING_MODE_CONTROL
        ]
        self.assertEqual(2, len(rows))
        parameters = list(inspect.signature(inventory._known_phone_mapping).parameters)
        receipt_arguments = {
            name: {} if name in {"condition_e2e_receipts", "contact_pet_e2e_receipts"} else None
            for name in parameters[4:]
        }
        for row in rows:
            mapping = inventory._known_phone_mapping(
                row,
                inventory.DEFAULT_CHUMMER5_ROOT,
                PRESENTATION_ROOT,
                CORE_ROOT,
                **receipt_arguments,
            )
            self.assertEqual("partial_exact_saved_data", mapping["status"])
            self.assertEqual("VehicleWeaponFiringModePage", mapping["surface"])
            self.assertIn("typed direct VehicleWeapon", mapping["presenterMutation"])
            self.assertIn("five-value legacy allowlist", mapping["presenterMutation"])
            self.assertIn("zero Nuyen/Karma", mapping["presenterMutation"])
            self.assertIn("underbarrel weapons", mapping["coverageLimit"])
            self.assertEqual("scripted_not_executed", mapping["e2e"]["status"])
            self.assertEqual(
                "tests/run_api36_vehicle_weapon_firing_mode_e2e.py",
                mapping["e2e"]["ref"],
            )
            self.assertEqual("missing", mapping["tablet"]["status"])
        for marker in (
            "VehicleWeaponFiringModePage",
            "VehicleWeaponFiringModeEditRequest.cs",
            "CharacterVehicleWeaponFiringModeRules.cs",
            '"status": "partial_exact_saved_data" if implemented and legacy_exact else "missing"',
            "direct Vehicle/weapons/weapon selection only",
            "tests/run_api36_vehicle_weapon_firing_mode_e2e.py",
        ):
            self.assertIn(marker, source)

    def test_weapon_matrix_source_mapping_is_four_row_career_only_typed_partial_and_scripted(self) -> None:
        import inspect

        source = SCRIPT.read_text(encoding="utf-8")
        self.assertEqual({
            "cboWeaponGearAttack": "cboWeaponGearAttack_SelectedIndexChanged",
            "cboWeaponGearSleaze": "cboWeaponGearSleaze_SelectedIndexChanged",
            "cboWeaponGearDataProcessing": "cboWeaponGearDataProcessing_SelectedIndexChanged",
            "cboWeaponGearFirewall": "cboWeaponGearFirewall_SelectedIndexChanged",
        }, inventory.WEAPON_MATRIX_SWAP_CONTROLS)
        payload = json.loads(
            (REPO / "docs" / "ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json").read_text(
                encoding="utf-8"
            )
        )
        rows = [
            row for row in payload["rows"]
            if row["legacy"]["formOrControl"] in {"CharacterCreate", "CharacterCareer"}
            and row["legacy"]["controlName"] in inventory.WEAPON_MATRIX_SWAP_CONTROLS
        ]
        self.assertEqual(4, len(rows))
        self.assertEqual({"CharacterCareer"}, {
            row["legacy"]["formOrControl"] for row in rows
        })
        parameters = list(inspect.signature(inventory._known_phone_mapping).parameters)
        receipt_arguments = {
            name: {} if name in {"condition_e2e_receipts", "contact_pet_e2e_receipts"} else None
            for name in parameters[4:]
        }
        for row in rows:
            mapping = inventory._known_phone_mapping(
                row,
                inventory.DEFAULT_CHUMMER5_ROOT,
                PRESENTATION_ROOT,
                CORE_ROOT,
                **receipt_arguments,
            )
            self.assertEqual("partial_exact_saved_data", mapping["status"])
            self.assertEqual("WeaponMatrixSwapPage", mapping["surface"])
            self.assertEqual(
                "weapon-matrix-swap-changed-{stable-weapon-guid}",
                mapping["automationId"],
            )
            self.assertIn("typed direct root Weapon", mapping["presenterMutation"])
            self.assertIn("Career-only phase", mapping["presenterMutation"])
            self.assertIn("CharacterCreate exposes no corresponding", mapping["coverageLimit"])
            self.assertIn("Vehicle-owned Weapons", mapping["coverageLimit"])
            self.assertEqual("scripted_not_executed", mapping["e2e"]["status"])
            self.assertEqual(
                "tests/run_api36_weapon_matrix_swap_e2e.py",
                mapping["e2e"]["ref"],
            )
            self.assertEqual("missing", mapping["tablet"]["status"])
        for marker in (
            "WeaponMatrixSwapPage",
            "WeaponMatrixSwapEditRequest.cs",
            "CharacterWeaponMatrixSwapRules.cs",
            'LegacySurface = "CharacterCareer.treWeapons"',
            '"status": "partial_exact_saved_data" if implemented else "missing"',
            "CharacterCreate exposes no corresponding mutable controls",
            "tests/run_api36_weapon_matrix_swap_e2e.py",
        ):
            self.assertIn(marker, source)


if __name__ == "__main__":
    unittest.main()
