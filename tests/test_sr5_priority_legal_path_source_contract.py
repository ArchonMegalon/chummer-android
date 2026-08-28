from __future__ import annotations

from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]
NATIVE = ROOT / "src/Chummer.Android/Native"
BUILD_PAGE = NATIVE / "BuildPage.cs"
BASICS_PAGE = NATIVE / "CreationBasicsPage.cs"
PROJECTION = NATIVE / "CreationPriorityLegalPathProjection.cs"
FINALIZATION_PAGE = NATIVE / "CreationFinalizationPage.cs"
DRIVER = ROOT / "tests/run_api36_sr5_priority_legal_path_e2e.py"


class Sr5PriorityLegalPathSourceContractTests(unittest.TestCase):
    def test_dashboard_routes_only_to_specialized_contextual_pages(self) -> None:
        source = BUILD_PAGE.read_text(encoding="utf-8")
        for marker in (
            "OpenCreationBasicsAsync",
            "new CreationBasicsPage(Coordinator)",
            "OpenCreationPrerequisiteAsync",
            "new CreationPrerequisitePage(Coordinator)",
            "OpenCreationAttributesAsync",
            "new CreationAttributesPage(Coordinator)",
            "OpenCreationSkillsAsync",
            "new CreationSkillsPage(Coordinator)",
            "OpenCreationQualitiesAsync",
            "new CreationQualitiesPage(Coordinator)",
            "OpenCreationMagicResonanceAsync",
            "new CreationMagicResonancePage(Coordinator)",
            "OpenCreationContactsAsync",
            "new CreationContactsPage(Coordinator)",
            "OpenCreationResourcesAsync",
            "new CreationResourcesPage(",
            "OpenCreationIdentityAsync",
            "new OriginDossierPage(Coordinator)",
        ):
            self.assertIn(marker, source)
        for forbidden in (
            "new CharacterEditorPage",
            "new GenericEditPage",
            "new AttributeEditRequest",
        ):
            self.assertNotIn(forbidden, source)

    def test_identity_and_resources_routes_fail_closed(self) -> None:
        source = BUILD_PAGE.read_text(encoding="utf-8")
        self.assertIn("bool canOpenIdentity = identityStage", source)
        self.assertIn("&& stage.IsAvailable", source)
        self.assertIn("The typed Resources/overview presenters are unavailable", source)
        self.assertNotIn(": Task.CompletedTask;\n\n    private static string CreationContactsStageDetail", source)

    def test_basics_is_read_only_and_names_the_exact_missing_dependency(self) -> None:
        source = BASICS_PAGE.read_text(encoding="utf-8")
        for marker in (
            "creation-basics-authority",
            "rules.GameEdition",
            "rules.Settings",
            "snapshot.BuildMethod",
            "snapshot.SourceDigest",
            "creation-basics-sourcebooks-contract-unavailable",
            "no typed creation settings-profile/sourcebook selection contract",
        ):
            self.assertIn(marker, source)
        for forbidden in (
            "Apply",
            "Confirm",
            "settings.xml#",
            "CharacterSettings",
            "SourcebookSummary",
        ):
            self.assertNotIn(forbidden, source)

    def test_finalization_readiness_is_wholly_core_projected(self) -> None:
        projection = PROJECTION.read_text(encoding="utf-8")
        build_page = BUILD_PAGE.read_text(encoding="utf-8")
        finalization = FINALIZATION_PAGE.read_text(encoding="utf-8")
        for marker in (
            "CharacterCreationFinalizationState",
            "step.Blockers",
            "step.SourceAnchorIds",
            "state.Binding.ContentRevision",
            "state.SnapshotDigest",
            "state.CanReview",
            "CharacterCreationFinalizationOutcomes.Available",
        ):
            self.assertIn(marker, projection)
        for marker in (
            "creation-finalization-readiness",
            "creation-finalization-authority-blocked",
            "creation-finalization-authority-ready",
            "creation-finalization-step-",
            "CreationPriorityLegalPathProjection.From(authority)",
        ):
            self.assertIn(marker, build_page)
        self.assertIn("explicit, atomic confirmation", finalization)
        self.assertNotIn("CharacterCreationWizardSnapshot snapshot", projection)

    def test_driver_is_development_only_and_does_not_seed_rule_state(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        for marker in (
            "Development-only",
            '"status": "development-proof"',
            '"releaseAuthority": False',
            "no fallback is allowed",
        ):
            self.assertIn(marker, source)
        for forbidden in (
            "pm clear",
            "run-as",
            "AuxiliaryState",
            "CharacterCreationFinalizationService",
            "settings.xml",
        ):
            self.assertNotIn(forbidden, source)


if __name__ == "__main__":
    unittest.main()
