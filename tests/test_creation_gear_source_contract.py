from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PRESENTATION_ROOT = ROOT.parent / "chummer-presentation"
PAGE = ROOT / "src/Chummer.Android/Native/CreationGearPage.cs"
RESOURCES_PAGE = ROOT / "src/Chummer.Android/Native/CreationResourcesPage.cs"
MAUI_PROGRAM = ROOT / "src/Chummer.Android/MauiProgram.cs"
BUILD_PAGE = ROOT / "src/Chummer.Android/Native/BuildPage.cs"
PRESENTER = PRESENTATION_ROOT / "Chummer.Presentation/Overview/CharacterCreationGearInteractionPresenter.cs"


def source(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class CreationGearSourceContractTests(unittest.TestCase):
    def test_phone_consumes_typed_renderer_neutral_presenter(self) -> None:
        page = source(PAGE)
        presenter = source(PRESENTER)
        self.assertIn("interface ICharacterCreationGearInteractionPresenter", presenter)
        for call in ("_gear.Load(", "_gear.Prepare(", "_gear.Confirm("):
            self.assertIn(call, page)
        self.assertNotIn("XDocument", page)
        self.assertNotIn("XElement", page)
        self.assertNotIn("PackageCost *", page)
        self.assertNotIn("new CharacterCreationGearBudget", page)
        self.assertNotIn("new CharacterCreationGearLine", page)

    def test_catalog_surfaces_exact_and_unsupported_core_rows(self) -> None:
        page = source(PAGE)
        for expression in (
            "state.Authority.Options",
            "option.IsSelectable",
            "option.PricingIsExact",
            "option.AvailabilityIsExact",
            "option.PackageCost",
            "option.PackageQuantity",
            "option.Availability",
            "option.Legality",
            "option.SourceBook",
            "option.Page",
            "option.Blockers.FirstOrDefault()",
            "CharacterCreationGearBlockers.UnsupportedSemantics",
        ):
            self.assertIn(expression, page)
        for automation_id in (
            "creation-gear-page",
            "creation-gear-search",
            "creation-gear-catalog-range",
            "creation-gear-catalog-previous",
            "creation-gear-catalog-next",
            "-option-id",
            "-option-digest",
        ):
            self.assertIn(automation_id, page)

    def test_basket_is_stable_id_quantity_only_and_bounded(self) -> None:
        page = source(PAGE)
        self.assertIn("new CharacterCreationGearSelection(item.Key, item.Value)", page)
        self.assertIn("state.Authority.MaximumBasketLines", page)
        self.assertIn("state.Authority.MaximumQuantityPerLine", page)
        self.assertIn("current.Count <= maximumLines", page)
        self.assertIn("item.Value <= maximumQuantity", page)
        self.assertIn("OrderBy(item => item.Key, StringComparer.Ordinal)", page)

    def test_preview_and_explicit_confirm_are_separate_and_fail_closed(self) -> None:
        page = source(PAGE)
        for automation_id in (
            "creation-gear-preview",
            "creation-gear-preview-page",
            "creation-gear-preview-budget",
            "creation-gear-confirm",
            "creation-gear-confirm-authority",
            "creation-gear-confirm-receipt",
            "creation-gear-reopen",
        ):
            self.assertIn(automation_id, page)
        self.assertIn("ExplicitlyConfirmed: true", page)
        self.assertIn("CreationGearPhoneAuthority.PreparedMatches", page)
        self.assertIn("CreationGearPhoneAuthority.ReceiptMatches", page)
        self.assertIn("CreationGearPhoneAuthority.RefreshedStateMatches", page)
        self.assertIn("await _overview.LoadAsync(receipt.WorkspaceId", page)
        self.assertIn("CharacterDocumentChanged", page)

    def test_physical_evidence_exposes_full_revision_and_digest_values(self) -> None:
        page = source(PAGE)
        for automation_id in (
            "creation-gear-binding-workspace-revision",
            "creation-gear-binding-content-revision",
            "creation-gear-binding-saved-revision",
            "creation-gear-binding-resources-draft-revision",
            "creation-gear-binding-raw-character-xml-digest",
            "creation-gear-binding-auxiliary-state-digest",
            "creation-gear-binding-resources-draft-digest",
            "creation-gear-binding-snapshot-digest",
            "creation-gear-preview-digest",
            "creation-gear-preview-state-snapshot-digest",
            "creation-gear-receipt-workspace-revision",
            "creation-gear-receipt-saved-revision",
            "creation-gear-receipt-resources-draft-revision",
            "creation-gear-receipt-raw-character-xml-digest",
            "creation-gear-receipt-command-digest",
            "creation-gear-receipt-draft-digest",
            "creation-gear-receipt-digest",
        ):
            self.assertIn(automation_id, page)

    def test_resources_entry_is_gated_by_confirmed_resources_and_typed_presenter(self) -> None:
        page = source(RESOURCES_PAGE)
        self.assertIn("state.PendingDraft is not null && _gear is not null", page)
        self.assertIn("new CreationGearPage(Coordinator, _gear!, _overview)", page)
        self.assertIn("creation-resources-open-gear", page)

    def test_dependency_injection_registers_real_typed_service(self) -> None:
        text = source(MAUI_PROGRAM)
        self.assertIn("AddSingleton<ICharacterCreationGearInteractionPresenter>", text)
        self.assertIn("provider.GetRequiredService<ICharacterCreationGearService>()", text)
        self.assertNotIn("UnavailableCharacterCreationGear", text)

    def test_build_page_injects_gear_without_owning_another_navigation_stage(self) -> None:
        text = source(BUILD_PAGE)
        self.assertIn("ICharacterCreationGearInteractionPresenter? gearPresenter = null", text)
        self.assertIn("_gearPresenter = gearPresenter", text)
        self.assertRegex(
            text,
            r"new CreationResourcesPage\(\s*Coordinator,\s*_resourcesPresenter,"
            r"\s*_overviewPresenter,\s*_gearPresenter,\s*authority\)",
        )
        self.assertNotIn("CreationDashboardAuthorityPhase.Gear", text)


if __name__ == "__main__":
    unittest.main()
