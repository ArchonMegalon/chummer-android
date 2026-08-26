import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
ADAPTER = (
    REPO_ROOT
    / "src"
    / "Chummer.Android"
    / "Native"
    / "AndroidCharacterCareerSkillGroupAdvanceWorkspace.cs"
)
COMPOSITION = REPO_ROOT / "src" / "Chummer.Android" / "MauiProgram.cs"


class SkillGroupAtomicWorkspaceSourceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.adapter = ADAPTER.read_text(encoding="utf-8")
        cls.composition = COMPOSITION.read_text(encoding="utf-8")

    def test_adapter_owns_the_core_workspace_contract_without_presentation_fallback(self) -> None:
        self.assertIn("ICharacterCareerSkillGroupAdvanceWorkspace", self.adapter)
        self.assertIn("IWorkspaceStore", self.adapter)
        self.assertNotIn("Chummer.Presentation", self.adapter)
        self.assertNotIn("WorkspaceXmlMutationCatalog", self.adapter)
        self.assertNotIn("ApplyCareerSkillGroupAdvance", self.adapter)

    def test_commit_is_one_document_and_checkpoint_cas_with_durable_recovery(self) -> None:
        self.assertIn("ReplaceWorkspaceDocumentAndCheckpoint", self.adapter)
        self.assertNotIn("SaveCheckpoint(", self.adapter)
        self.assertIn("RecoverUnknownCommit", self.adapter)
        self.assertIn("LookupUnderGate", self.adapter)
        self.assertIn("transaction_id_claimed_by_different_command", self.adapter)

    def test_receipt_ledger_binds_command_quote_and_applied_result(self) -> None:
        self.assertIn('new XElement("commanddigest", commandDigest)', self.adapter)
        self.assertIn('new XElement("bindingdigest", bindingDigest)', self.adapter)
        self.assertIn('new XElement("appliedresultdigest", resultDigest)', self.adapter)
        self.assertIn("TryComputeBindingDigest", self.adapter)
        self.assertIn("TryComputeResultDigest", self.adapter)
        self.assertIn("TryCreateReceipt", self.adapter)

    def test_android_composition_overrides_core_fail_closed_default_before_runtime(self) -> None:
        registration = (
            "AddSingleton<ICharacterCareerSkillGroupAdvanceWorkspace,\n"
            "            AndroidCharacterCareerSkillGroupAdvanceWorkspace>()"
        )
        self.assertIn(registration, self.composition)
        self.assertLess(
            self.composition.index(registration),
            self.composition.index("AddChummerLocalRuntimeClient("),
        )


if __name__ == "__main__":
    unittest.main()
