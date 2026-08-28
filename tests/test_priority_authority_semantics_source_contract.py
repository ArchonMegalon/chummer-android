from __future__ import annotations

import re
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
NATIVE = ROOT / "src" / "Chummer.Android" / "Native"


class PriorityAuthoritySemanticsSourceContractTests(unittest.TestCase):
    def test_wp2_exact_ids_are_emitted_by_production_pages(self) -> None:
        prerequisite = (NATIVE / "CreationPrerequisitePage.cs").read_text(encoding="utf-8")
        finalization = (NATIVE / "CreationFinalizationPage.cs").read_text(encoding="utf-8")
        build = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")

        self.assertIn('"creation-prerequisite-build-method-id"', prerequisite)
        for automation_id in (
            "creation-finalization-content-revision",
            "creation-finalization-plan-digest",
            "creation-finalization-preview-digest",
            "creation-finalization-receipt-previous-content-revision",
            "creation-finalization-receipt-content-revision",
            "creation-finalization-receipt-saved-revision",
            "creation-finalization-receipt-build-method",
            "creation-finalization-receipt-plan-digest",
            "creation-finalization-receipt-preview-digest",
            "creation-finalization-receipt-digest",
        ):
            self.assertEqual(1, finalization.count(f'"{automation_id}"'))
        self.assertEqual(1, build.count('"phone-workspace-creation-receipt-digest"'))

    def test_authority_values_come_directly_from_typed_full_values(self) -> None:
        prerequisite = (NATIVE / "CreationPrerequisitePage.cs").read_text(encoding="utf-8")
        finalization = (NATIVE / "CreationFinalizationPage.cs").read_text(encoding="utf-8")
        build = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")

        self.assertRegex(
            prerequisite,
            r'"creation-prerequisite-build-method-id",\s*state\.BuildMethod',
        )
        expected_bindings = {
            "creation-finalization-content-revision": r"_review\.Binding\.ContentRevision",
            "creation-finalization-plan-digest": r"_review\.Plan\.PlanDigest",
            "creation-finalization-preview-digest": r"_review\.PreviewDigest",
            "creation-finalization-receipt-previous-content-revision": r"_receipt\.PreviousContentRevision",
            "creation-finalization-receipt-content-revision": r"_receipt\.ContentRevision",
            "creation-finalization-receipt-saved-revision": r"_receipt\.SavedRevision",
            "creation-finalization-receipt-build-method": r"_receipt\.BuildMethod",
            "creation-finalization-receipt-plan-digest": r"_receipt\.PlanDigest",
            "creation-finalization-receipt-preview-digest": r"_receipt\.PreviewDigest",
            "creation-finalization-receipt-digest": r"_receipt\.ReceiptDigest",
        }
        for automation_id, value_pattern in expected_bindings.items():
            self.assertRegex(
                finalization,
                rf'"{re.escape(automation_id)}",\s*{value_pattern}',
            )
        self.assertRegex(
            build,
            r'"phone-workspace-creation-receipt-digest",\s*persistedReceipt\.ReceiptDigest',
        )

    def test_semantic_overlay_is_non_visual_exact_and_fails_closed(self) -> None:
        source = (NATIVE / "NativeAuthoritySemantics.cs").read_text(encoding="utf-8")
        projection = (NATIVE / "CreationPriorityLegalPathProjection.cs").read_text(encoding="utf-8")
        for marker in (
            "Text = string.Empty",
            "InputTransparent = true",
            "SemanticProperties.SetDescription(semantic, value.ExactValue)",
            "NormalizeMachineDigestPayload(value)",
            "value <= 0",
            "string.IsNullOrWhiteSpace(value)",
            "Authority AutomationIds must be unique",
        ):
            self.assertIn(marker, source)
        for forbidden in (
            "value[..",
            "Substring(",
            "Short(",
            "ShortDigest(",
            "Text = value.ExactValue",
        ):
            self.assertNotIn(forbidden, source)
        self.assertIn("CharacterCreationFinalizationDigest.IsCanonical(value)", projection)
        self.assertIn('value!["sha256:".Length..]', projection)

    def test_visible_short_values_are_not_used_as_machine_authority(self) -> None:
        source = (NATIVE / "CreationFinalizationPage.cs").read_text(encoding="utf-8")
        self.assertIn("Short(_review.Plan!.PlanDigest)", source)
        self.assertIn("Short(_review.PreviewDigest)", source)
        self.assertIn("Short(_receipt.ReceiptDigest)", source)
        self.assertIn("Short(_receipt.PlanDigest)", source)
        self.assertIn('$"plan {Short(_review.Plan!.PlanDigest)}', source)

        for call in re.findall(
            r"NativeAuthoritySemantics\.(?:Digest|Identifier|PositiveRevision)\([^;]+?\)",
            source,
            flags=re.DOTALL,
        ):
            self.assertNotIn("Short(", call)
            self.assertNotIn("ShortDigest(", call)
            self.assertNotIn("…", call)

    def test_persisted_digest_is_loaded_from_typed_workspace_ledger_and_is_career_only(self) -> None:
        coordinator = (NATIVE / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        projection = (NATIVE / "CreationPriorityLegalPathProjection.cs").read_text(encoding="utf-8")
        build = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")

        for marker in (
            "State.Profile?.Created != true",
            "_creationFinalizationService.Load(new(workspaceId))",
            "ResolvePersistedPriorityReceipt(",
            "LastReceipt: { } receipt",
            "CharacterCreationFinalizationOutcomes.Blocked",
            "CharacterCreationFinalizationBlockers.CharacterAlreadyCreated",
            "CharacterCreationFinalizationDigest.ComputeReceiptDigest(receipt)",
            "receipt.ContentRevision != contentRevision",
            "receipt.SavedRevision != savedRevision",
        ):
            self.assertIn(marker, coordinator + projection)
        self.assertIn("Coordinator.LoadPersistedPriorityCreationReceipt()", build)
        for forbidden in (
            "Preferences.Default.Set",
            "ReceiptDigest =",
            "ShortDigest(persistedReceipt",
            "Short(persistedReceipt",
        ):
            self.assertNotIn(forbidden, build)

    def test_authority_surface_does_not_expose_secret_receipt_fields(self) -> None:
        finalization = (NATIVE / "CreationFinalizationPage.cs").read_text(encoding="utf-8")
        build = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")
        for secret in (
            "_receipt.ReceiptId",
            "_receipt.IdempotencyKeyDigest",
            "_receipt.CommandDigest",
            "_receipt.RawCharacterXmlDigest",
            "_receipt.PreviousRawCharacterXmlDigest",
            "_receipt.PreviousAuxiliaryStateDigest",
            "_receipt.AuthorityDigest",
            "_receipt.PreviousReceiptDigest",
        ):
            self.assertNotIn(secret, finalization + build)

    def test_existing_visible_root_and_stage_ids_are_preserved(self) -> None:
        prerequisite = (NATIVE / "CreationPrerequisitePage.cs").read_text(encoding="utf-8")
        finalization = (NATIVE / "CreationFinalizationPage.cs").read_text(encoding="utf-8")
        build = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")
        for marker in (
            'AutomationId = "creation-prerequisite-page"',
            'border.AutomationId = "creation-prerequisite-method"',
            'AutomationId = "creation-finalization-page"',
            'binding.AutomationId = "creation-finalization-binding"',
            'AutomationId = "creation-finalization-receipt-page"',
            'card.AutomationId = "creation-finalization-receipt"',
            'new("phone-runner-sheet", "Career runner")',
        ):
            self.assertIn(marker, prerequisite + finalization + build)


if __name__ == "__main__":
    unittest.main()
