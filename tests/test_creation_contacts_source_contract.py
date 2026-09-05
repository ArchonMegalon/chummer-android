from pathlib import Path
import unittest


REPO = Path(__file__).resolve().parents[1]
NATIVE = REPO / "src/Chummer.Android/Native"


class CreationContactsSourceContractTests(unittest.TestCase):
    def test_coordinator_uses_typed_presenter_and_strict_confirm_recovery(self) -> None:
        source = (NATIVE / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        for marker in (
            "ICharacterCreationContactsInteractionPresenter creationContactsPresenter",
            "_creationContactsPresenter.Load(State)",
            "LoadCreationContacts()",
            "PrepareCreationContact(",
            "_creationContactsPresenter.Prepare(State, input)",
            "ConfirmCreationContactAsync(",
            "WithWorkspaceActivationGateAsync(",
            "prepared.IdempotencyKey",
            "prepared.PreviewDigest",
            "ExplicitlyConfirmed: true",
            "_creationContactsPresenter.Confirm(State, confirmation)",
            "_creationContactsPresenter.LookupReceipt(State, prepared.IdempotencyKey)",
            "await _presenter.LoadAsync(receipt.WorkspaceId, cancellationToken)",
            "CreationContactsPhoneAuthority.ReceiptMatches(",
            "CreationContactsPhoneAuthority.RefreshedStateMatches(",
            'expectedPayloadSha256: receipt.ContentDigestAfter["sha256:".Length..]',
            "CreationContactsPhoneAuthority.IsBound(",
        ):
            self.assertIn(marker, source)

        region = source[
            source.index("LoadCreationContacts()") : source.index(
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

    def test_phone_draft_is_contact_and_snapshot_bound_with_complete_identity(self) -> None:
        source = (NATIVE / "CreationContactsPhoneDraft.cs").read_text(encoding="utf-8")
        for marker in (
            "CharacterCreationContactIdentity",
            "Name",
            "Role",
            "Location",
            "Notes",
            "CustomName",
            "Metatype",
            "Gender",
            "Age",
            "ContactType",
            "PreferredPayment",
            "HobbiesVice",
            "PersonalLife",
            "GroupName",
            "state.SnapshotDigest",
            "contact.ContactDigest",
            "CharacterCreationContactFieldIds.All",
            "new CharacterCreationContactEditInput(",
            "Identity: IdentityChanged",
            "CreationContactsPhoneAuthority.BindingEquals",
            "IsRawLowerDigest(state.Binding.AuxiliaryStateDigest)",
            "WritePlanMatchesPrepared",
            "WritePlanEquals",
            "Enumerable.Range(1, plan.Operations.Count)",
            "CharacterCreationContactSourceAnchors.All",
            "prepared.IdempotencyKey.Length is > 0 and <= 200",
        ):
            self.assertIn(marker, source)
        for forbidden in (
            "System.Xml",
            "Preferences.Default",
            "HttpClient",
            "Dictionary<string, object>",
            "Guid.NewGuid",
        ):
            self.assertNotIn(forbidden, source)

    def test_list_edit_preview_pages_are_dedicated_phone_wizard_surfaces(self) -> None:
        listing = (NATIVE / "CreationContactsPage.cs").read_text(encoding="utf-8")
        edit = (NATIVE / "CreationContactEditPage.cs").read_text(encoding="utf-8")
        preview = (NATIVE / "CreationContactPreviewPage.cs").read_text(encoding="utf-8")
        build = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")

        for marker in (
            'AutomationId = "creation-contacts-page"',
            'AutomationId = "creation-contacts-binding"',
            '"creation-contacts-budget"',
            '"creation-contacts-high-places-budget"',
            '"creation-contact-item-{contact.ContactId:N}"',
            "new CreationContactEditPage(",
        ):
            self.assertIn(marker, listing)

        for marker in (
            'AutomationId = "creation-contact-edit-page"',
            '"creation-contact-field-{field.FieldId}"',
            "CharacterCreationContactFieldIds.All",
            "field.IsEditable",
            "field.LegalOptions",
            "_draft.ToInput(state, contact)",
            'AutomationId = "creation-contact-preview"',
            "new CreationContactPreviewPage(",
        ):
            self.assertIn(marker, edit)

        for marker in (
            'AutomationId = "creation-contact-preview-page"',
            '"creation-contact-preview-digest"',
            '"creation-contact-plan-digest"',
            '"creation-contact-write-{operation.Order}-{operation.FieldId}"',
            'AutomationId = "creation-contact-explicit-confirm"',
            'AutomationId = "creation-contact-confirm"',
            'AutomationId = "creation-contact-confirm-receipt"',
            '"creation-contact-receipt-id"',
            '"creation-contact-receipt-digest"',
            '"creation-contact-receipt-content-before"',
            '"creation-contact-receipt-content-after"',
            "Coordinator.ConfirmCreationContactAsync(",
            'AutomationId = "creation-contact-back-to-build"',
            "Navigation.PopToRootAsync(animated: false)",
        ):
            self.assertIn(marker, preview)

        self.assertIn("if (Coordinator.State.Profile.Created == false)", build)
        self.assertIn("CharacterCreationWizardStepIds.ContactsLifestyles", build)
        self.assertIn("new CreationContactsPage(Coordinator, authority)", build)
        self.assertIn("CreationPageAuthorityCache.Resolve(", listing)
        for marker in (
            "_creationContactsQueue",
            "CreationDashboardAuthorityPhase.Contacts",
            "Coordinator.LoadCreationContacts",
            "AcceptCreationContacts",
            "ContactsFailureReason",
            "CreationContactsPhoneAuthority.IsReady",
        ):
            self.assertIn(marker, build)
        creation_gate = build[
            build.index("if (Coordinator.State.Profile.Created == false)") : build.index(
                'Title = "Sheet";'
            )
        ]
        self.assertIn("AddCreationWizardDashboard();", creation_gate)
        self.assertIn("return;", creation_gate)

        combined = listing + edit + preview
        for forbidden in (
            "NativeCommandPage",
            "CollectionEditor",
            "System.Xml",
            "SaveAsync(",
            "ApplyCollectionMutationAsync",
            "ContactPet",
            "RookConversationPage",
            "Build Ghost",
        ):
            self.assertNotIn(forbidden, combined)

    def test_dependency_injection_registers_core_and_presenter_authorities(self) -> None:
        source = (REPO / "src/Chummer.Android/MauiProgram.cs").read_text(encoding="utf-8")
        for marker in (
            "ICharacterCreationContactsService",
            "ICharacterCreationContactsInteractionPresenter",
            "CharacterCreationContactsInteractionPresenter",
            "IWorkspaceOverviewStateFactory",
            "WorkspaceOverviewStateFactory(",
            "provider.GetRequiredService<ICharacterCreationContactsService>()",
        ):
            self.assertIn(marker, source)

    def test_physical_driver_is_bound_only_to_the_dedicated_contacts_route(self) -> None:
        driver = (REPO / "tests/run_api36_sr5_creation_contacts_e2e.py").read_text(
            encoding="utf-8"
        )
        for marker in (
            'STAGE_ID = "creation-stage-contacts-lifestyles"',
            'CONTACT_ID = "50d92979-524d-4cb5-898e-196771e3c786"',
            '"creation-contact-field-name"',
            '"creation-contact-field-role"',
            '"creation-contact-preview"',
            '"creation-contact-explicit-confirm"',
            '"creation-contact-confirm"',
            '"creation-contact-confirm-receipt"',
            "validate_receipt_projection(",
            "shared.force_stop_and_launch_new_process(",
            "shared.require_restored_authority(saved, restored)",
        ):
            self.assertIn(marker, driver)
        self.assertNotIn("creation-stage-foundation", driver)
        self.assertNotIn("creation-foundation-page", driver)

if __name__ == "__main__":
    unittest.main()
