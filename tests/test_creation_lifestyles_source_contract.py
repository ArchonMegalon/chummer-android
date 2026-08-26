from pathlib import Path
import unittest


REPO = Path(__file__).resolve().parents[1]
NATIVE = REPO / "src/Chummer.Android/Native"
PHYSICAL_DRIVER = REPO / "tests/run_api36_sr5_creation_lifestyles_e2e.py"


class CreationLifestylesSourceContractTests(unittest.TestCase):
    def test_coordinator_uses_typed_presenter_and_strict_receipt_recovery(self) -> None:
        source = (NATIVE / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        for marker in (
            "ICharacterCreationLifestylesInteractionPresenter creationLifestylesPresenter",
            "_creationLifestylesPresenter.Load(State)",
            "LoadCreationLifestyles()",
            "PrepareCreationLifestyle(",
            "_creationLifestylesPresenter.Prepare(State, input)",
            "ConfirmCreationLifestyleAsync(",
            "WithWorkspaceActivationGateAsync(",
            "prepared.IdempotencyKey",
            "prepared.PreviewDigest",
            "ExplicitlyConfirmed: true",
            "_creationLifestylesPresenter.Confirm(State, confirmation)",
            "_creationLifestylesPresenter.LookupReceipt(State, prepared.IdempotencyKey)",
            "CreationLifestylesPhoneAuthority.ReceiptMatches(",
            "CreationLifestylesPhoneAuthority.RefreshedStateMatches(",
            'expectedPayloadSha256: receipt.ContentDigestAfter["sha256:".Length..]',
        ):
            self.assertIn(marker, source)

        region = source[
            source.index("LoadCreationLifestyles()") : source.index(
                "LoadCreationPrerequisite()"
            )
        ]
        for forbidden in (
            "System.Xml",
            "WorkspaceXmlMutationCatalog",
            "SaveAsync(",
            "UpdateMetadataAsync",
            "ApplyCollectionMutationAsync",
        ):
            self.assertNotIn(forbidden, region)

    def test_phone_draft_is_catalog_and_snapshot_bound(self) -> None:
        source = (NATIVE / "CreationLifestylesPhoneDraft.cs").read_text(encoding="utf-8")
        for marker in (
            "CharacterCreationLifestyleConfiguration",
            "state.SnapshotDigest",
            "ResolveUniqueSelectableOption",
            "ResolveUniqueSelectableQuality",
            "CharacterCreationLifestyleMutationKinds.Create",
            "CharacterCreationLifestyleMutationKinds.Edit",
            "CharacterCreationLifestyleMutationKinds.Delete",
            "DeterministicQualityIdentity",
            "CharacterCreationLifestylesRules.ComputeProjectionDigest",
            "CharacterCreationLifestylesRules.ComputePlanDigest",
            "CharacterCreationLifestylesRules.ComputePreviewDigest",
            "CharacterCreationLifestylesRules.ComputeReceiptDigest",
            "PreservesUntouchedSiblingState",
            "PreservesNestedState",
        ):
            self.assertIn(marker, source)
        for forbidden in (
            "System.Xml",
            "XDocument",
            "XElement",
            "WorkspaceXmlMutationCatalog",
            "Dictionary<string, object>",
            "Preferences.Default",
        ):
            self.assertNotIn(forbidden, source)

    def test_catalog_configure_preview_pages_are_dedicated_phone_surfaces(self) -> None:
        listing = (NATIVE / "CreationLifestylesPage.cs").read_text(encoding="utf-8")
        edit = (NATIVE / "CreationLifestyleEditPage.cs").read_text(encoding="utf-8")
        preview = (NATIVE / "CreationLifestylePreviewPage.cs").read_text(encoding="utf-8")
        contacts = (NATIVE / "CreationContactsPage.cs").read_text(encoding="utf-8")

        for marker in (
            'AutomationId = "creation-lifestyles-page"',
            '"creation-lifestyles-binding"',
            '"creation-lifestyles-budget"',
            '"creation-lifestyle-item-{lifestyle.Configuration.LifestyleId:N}"',
            '"creation-lifestyle-catalog-{Token(option.OptionId)}"',
            "new CreationLifestyleEditPage(",
        ):
            self.assertIn(marker, listing)

        for marker in (
            'AutomationId = "creation-lifestyle-edit-page"',
            '"creation-lifestyle-base-option"',
            '"creation-lifestyle-name"',
            '"creation-lifestyle-style"',
            '"creation-lifestyle-increments"',
            '"creation-lifestyle-percentage"',
            '"creation-lifestyle-quality-{Token(option.OptionId)}"',
            "_draft.ToInput(state)",
            "_draft.ToDeleteInput(state)",
            'AutomationId = "creation-lifestyle-preview"',
            "new CreationLifestylePreviewPage(",
        ):
            self.assertIn(marker, edit)

        for marker in (
            'AutomationId = "creation-lifestyle-preview-page"',
            '"creation-lifestyle-preview-digest"',
            '"creation-lifestyle-plan-digest"',
            "PreservesUntouchedSiblingState",
            "PreservesNestedState",
            'AutomationId = "creation-lifestyle-explicit-confirm"',
            'AutomationId = "creation-lifestyle-confirm"',
            'AutomationId = "creation-lifestyle-confirm-receipt"',
            "Coordinator.ConfirmCreationLifestyleAsync(_prepared)",
            'AutomationId = "creation-lifestyle-back-to-build"',
        ):
            self.assertIn(marker, preview)

        self.assertIn('automationId: "creation-contacts-open-lifestyles"', contacts)
        self.assertIn("new CreationLifestylesPage(Coordinator)", contacts)

        combined = listing + edit + preview
        for forbidden in (
            "System.Xml",
            "SaveAsync(",
            "ApplyCollectionMutationAsync",
            "NativeCommandPage",
            "CollectionEditor",
        ):
            self.assertNotIn(forbidden, combined)

    def test_dependency_injection_registers_core_and_presenter_authorities(self) -> None:
        source = (REPO / "src/Chummer.Android/MauiProgram.cs").read_text(encoding="utf-8")
        for marker in (
            "ICharacterCreationLifestylesService",
            "ICharacterCreationLifestylesInteractionPresenter",
            "CharacterCreationLifestylesInteractionPresenter",
            "provider.GetRequiredService<ICharacterCreationLifestylesService>()",
        ):
            self.assertIn(marker, source)

    def test_physical_driver_covers_catalog_create_confirm_and_restart_authority(self) -> None:
        source = PHYSICAL_DRIVER.read_text(encoding="utf-8")
        for marker in (
            'STAGE_ID = "creation-stage-contacts-lifestyles"',
            'CATALOG_PREFIX = "creation-lifestyle-catalog-"',
            '"creation-lifestyles-binding"',
            '"creation-lifestyle-edit-binding"',
            '"creation-lifestyle-preview-digest"',
            '"creation-lifestyle-plan-digest"',
            '"creation-lifestyle-write-1-create"',
            '"creation-lifestyle-explicit-confirm"',
            '"creation-lifestyle-confirm"',
            '"creation-lifestyle-confirm-receipt"',
            "validate_receipt_projection(receipt, imported=imported, saved=saved)",
            "shared.require_restored_authority(saved, restored)",
            "after_restart = assert_reopened_lifestyle(",
            '"sourceGraphRecheckedAfterRun": True',
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, source)
        self.assertNotIn("creation-stage-foundation", source)
        self.assertNotIn("creation-foundation-page", source)


if __name__ == "__main__":
    unittest.main()
