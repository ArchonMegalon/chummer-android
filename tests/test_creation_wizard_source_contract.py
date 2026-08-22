import ast
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
NATIVE = REPO / "src" / "Chummer.Android" / "Native"
DRIVER = REPO / "tests" / "run_api36_creation_wizard_foundation_e2e.py"


class CreationWizardSourceContractTests(unittest.TestCase):
    def test_uncreated_build_is_gated_before_exhaustive_editor(self) -> None:
        source = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")
        start = source.index("if (Coordinator.State.Profile.Created == false)")
        end = source.index("SetExhaustiveActionsVisible(true);", start)
        creation_branch = source[start:end]

        self.assertIn("SetExhaustiveActionsVisible(false);", creation_branch)
        self.assertIn("AddCreationWizardDashboard();", creation_branch)
        self.assertIn("return;", creation_branch)
        for forbidden in ("AddDossier();", "AddBuildAreas();", "AddTools();"):
            self.assertNotIn(forbidden, creation_branch)

        self.assertLess(end, source.index("AddDossier();", end))
        self.assertIn('automationId: "build-free-sprite-conversion"', source)
        self.assertGreater(source.index('automationId: "build-free-sprite-conversion"'), end)

    def test_dashboard_is_projection_only_and_life_modules_fail_closed(self) -> None:
        source = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")
        for marker in (
            "Coordinator.State.CreationWizard",
            "CharacterCreationWizardSnapshot",
            "snapshot.Budgets",
            "snapshot.Steps",
            "snapshot.CompletionBlockers",
            "active?.LegalNextStepIds",
            '"creation-wizard-rook"',
            '"creation-wizard-binding"',
            '"creation-wizard-life-modules-blocked"',
            "CharacterCreationBuildMethods.LifeModules",
            "lifeModuleStage.IsAvailable",
            "will not substitute or claim",
            "No authoritative budgets are available",
        ):
            self.assertIn(marker, source)
        self.assertNotIn("new CharacterCreationBudgetState", source)
        self.assertNotIn("BuildNewCharacterKarmaWorkflowDialog", source)

    def test_attributes_use_narrow_existing_typed_mutation_path(self) -> None:
        dashboard = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")
        page = (NATIVE / "CreationAttributesPage.cs").read_text(encoding="utf-8")
        editor = (NATIVE / "AttributeEditPage.cs").read_text(encoding="utf-8")

        self.assertIn('string.Equals(tab.Id, "tab-attributes"', dashboard)
        self.assertIn("new CreationAttributesPage(Coordinator)", dashboard)
        self.assertIn("AttributeWorkbenchProjector.BuildRows", page)
        self.assertIn("new AttributeEditPage(Coordinator, row)", page)
        self.assertIn("CharacterCreationBudgetIds.NormalAttributes", page)
        self.assertIn("if (budget.IsExact)", page)
        self.assertIn("if (!AddBudget(snapshot))", page)
        self.assertIn("return budget.IsExact;", page)
        self.assertIn("will not guess", page)
        self.assertIn("AttributeEditRequest", editor)
        for forbidden in ("NativeCommandPage", "AddSectionActions", "AddQuickActions"):
            self.assertNotIn(forbidden, page)

    def test_rook_is_workspace_revision_digest_bound_and_non_mutating(self) -> None:
        store = (NATIVE / "RookConversation.cs").read_text(encoding="utf-8")
        page = (NATIVE / "RookConversationPage.cs").read_text(encoding="utf-8")
        coordinator = (NATIVE / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")

        for marker in (
            "Dictionary<string, Thread>",
            'PreferencePrefix = "chummer.android.rook-thread.v1."',
            "Preferences.Default.Get",
            "Preferences.Default.Set",
            "MaximumMessagesPerThread = 80",
            "IsValidPersistedMessage",
            "CharacterCreationWizardStepIds.LifeModules",
            "lifeModuleStage.IsAvailable",
            "snapshot.WorkspaceId",
            "snapshot.WorkspaceRevision",
            "snapshot.SnapshotDigest",
            "IsStale(long currentRevision, string? currentSnapshotDigest)",
            "RookLocalGroundedResponder.Answer",
            "Local grounded fallback",
        ):
            self.assertIn(marker, store)
        for marker in (
            '"rook-local-grounded-fallback"',
            '"rook-current-binding"',
            '"This answer belongs to an older runner revision.',
            "message.IsStale(snapshot.WorkspaceRevision, snapshot.SnapshotDigest)",
        ):
            self.assertIn(marker, page)
        self.assertIn("snapshot.WorkspaceRevision != State.ContentRevision", coordinator)
        self.assertIn("_rookConversations.AddGroundedTurn(snapshot, question)", coordinator)
        combined = store + page
        for forbidden in ("HttpClient", "BuildGhostAlicePacketLoader", "ApplyAttributeEditAsync", "SaveAsync("):
            self.assertNotIn(forbidden, combined)

    def test_completed_setup_routes_only_authoritative_uncreated_workspace(self) -> None:
        source = (NATIVE / "NativeDialogPage.cs").read_text(encoding="utf-8")
        route = source[source.index("bool routeToCreationWizard"):source.index("else", source.index("bool routeToCreationWizard"))]
        self.assertIn("CompleteNewCharacterWorkflowActionId", route)
        self.assertIn("_coordinator.State.WorkspaceId is not null", route)
        self.assertIn("_coordinator.State.Profile?.Created == false", route)
        self.assertIn("UsesTabletComposition: false", route)
        self.assertIn('await shell.GoToAsync("//build")', route)
        self.assertLess(route.index("await CloseAsync"), route.index('await shell.GoToAsync("//build")'))

    def test_api36_driver_is_scripted_but_not_executed_by_unit_tests(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('"status": "scripted_not_executed"', source)
        self.assertIn('device.wait("creation-wizard-dashboard"', source)
        self.assertNotIn('device.tap("Continue building"', source)
        self.assertIn('device.tap("creation-wizard-rook"', source)
        self.assertIn('device.set_text("rook-question"', source)
        self.assertIn('device.tap("rook-send-question"', source)
        self.assertIn("assert_same_binding", source)
        self.assertIn('device.shell("am", "force-stop", shared.PACKAGE)', source)
        self.assertIn('"rookTranscriptSurvivesProcessRestart": "pass"', source)
        self.assertNotIn('"profile": "tablet"', source)


if __name__ == "__main__":
    unittest.main()
