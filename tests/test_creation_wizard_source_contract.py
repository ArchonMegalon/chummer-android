import ast
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
NATIVE = REPO / "src" / "Chummer.Android" / "Native"
MAUI_PROGRAM = REPO / "src" / "Chummer.Android" / "MauiProgram.cs"
DRIVER = REPO / "tests" / "run_api36_creation_wizard_foundation_e2e.py"


class CreationWizardSourceContractTests(unittest.TestCase):
    def test_android_injects_authoritative_foundation_into_overview_state_factory(self) -> None:
        source = MAUI_PROGRAM.read_text(encoding="utf-8")
        runtime_registration = source.index("builder.Services.AddChummerLocalRuntimeClient(")
        factory_registration = source.index(
            "builder.Services.AddSingleton<IWorkspaceOverviewStateFactory>(provider =>"
        )
        presenter_registration = source.index(
            "builder.Services.AddSingleton<ICharacterOverviewPresenter, CharacterOverviewPresenter>();"
        )

        self.assertLess(runtime_registration, factory_registration)
        self.assertLess(factory_registration, presenter_registration)
        self.assertIn(
            "provider.GetRequiredService<ICharacterCreationFoundationService>()",
            source[factory_registration:presenter_registration],
        )
        self.assertIn(
            "builder.Services.AddSingleton<ICharacterCreationFoundationInteractionPresenter>(provider =>",
            source,
        )
        self.assertIn("new CharacterCreationFoundationInteractionPresenter(", source)

    def test_foundation_interaction_stays_behind_presentation_and_refreshes_overview(self) -> None:
        source = (NATIVE / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        for marker in (
            "ICharacterCreationFoundationInteractionPresenter foundationInteractionPresenter",
            "LoadCreationFoundation()",
            "PrepareCreationFoundation(",
            "ConfirmCreationFoundationAsync(",
            "_foundationInteractionPresenter.Load(State)",
            "_foundationInteractionPresenter.Prepare(State, input)",
            "_foundationInteractionPresenter.Confirm(State, confirmation)",
            "await _presenter.LoadAsync(receipt.WorkspaceId, cancellationToken)",
            "OverviewMatchesFoundationReceipt(State, result, receipt)",
            "CharacterCreationFoundationInteractionBlockers.RefreshAuthorityRequired",
            "foundation.PendingDraft is { } draft",
            "!draft.CharacterEffectsApplied",
        ):
            self.assertIn(marker, source)

    def test_phone_foundation_pages_render_authority_and_keep_explicit_preview(self) -> None:
        selection = (NATIVE / "CreationFoundationPage.cs").read_text(encoding="utf-8")
        preview = (NATIVE / "CreationFoundationPreviewPage.cs").read_text(encoding="utf-8")
        dashboard = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")

        for marker in (
            "Coordinator.LoadCreationFoundation()",
            "state.MetatypeOptions",
            "state.NationalityOptions",
            "nationality.Versions",
            "nationality.FollowUps.Concat(version?.FollowUps ?? [])",
            'string.Equals(prompt.InputKind, "select"',
            'string.Equals(prompt.InputKind, "single-select"',
            "option.SourceValue",
            "Unsupported follow-up kind:",
            "Requires authoritative metatype evaluation in Preview",
            "IsMetatypeEvaluationCandidate(",
            "CanEvaluateWithSelectedMetatype(",
            "HasExactCandidateIdentityCostAndSource(",
            "HasOnlyEligibilityAuthorityBlocker(",
            "CharacterCreationFoundationBlockers.CharacterEligibilityAuthorityRequired",
            "HasOnlyTypedMetatypeRequirements(",
            "requirement.RequiresCharacterAuthority",
            'string.Equals(requirement.Operator, "oneof"',
            'string.Equals(requirement.SubjectKind, "metatype"',
            "Coordinator.PrepareCreationFoundation(",
            "new CreationFoundationPreviewPage(Coordinator, prepared)",
            '"creation-foundation-prepare-preview"',
            '"creation-foundation-pending-draft"',
            '"creation-foundation-pending-compilation-status"',
            '"creation-foundation-pending-character-effects-applied"',
        ):
            self.assertIn(marker, selection)
        for forbidden in ('?? "Human"', "Picker", "SelectedIndex = 0"):
            self.assertNotIn(forbidden, selection)
        self.assertIn("bool selectable = option.IsEnabled || evaluationCandidate;", selection)
        self.assertIn("bool selectable = version.IsEnabled || evaluationCandidate;", selection)

        for marker in (
            "private readonly CharacterCreationFoundationPreparedPreview _prepared",
            "_prepared.PreviewDigest",
            "ExplicitlyConfirmed: true",
            "Coordinator.ConfirmCreationFoundationAsync(",
            "_prepared.LifeModuleBudgetBefore",
            "_prepared.LifeModuleBudgetAfter",
            "foreach (CharacterCreationFoundationDiffEntry diff in _prepared.Diff)",
            '"creation-foundation-preview-diff-',
            '"creation-foundation-confirm"',
            '"creation-foundation-character-effects-applied"',
            '"creation-foundation-compilation-status"',
            '"creation-foundation-save"',
        ):
            self.assertIn(marker, preview)
        self.assertLess(
            selection.index("Coordinator.PrepareCreationFoundation("),
            selection.index("new CreationFoundationPreviewPage(Coordinator, prepared)"),
        )
        self.assertNotIn("Coordinator.PrepareCreationFoundation", preview)

        for marker in (
            "HasAuthoritativeFoundationOptions()",
            "CharacterCreationWizardStepIds.Foundation",
            "CharacterCreationWizardStepIds.LifeModules",
            "new CreationFoundationPage(Coordinator)",
            "Coordinator.State.Profile?.Created == false",
            "!foundation.CharacterCreated",
        ):
            self.assertIn(marker, dashboard)

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
        self.assertIn("def assert_creation_editor_gated", source)
        self.assertIn('"build-career-create-expense"', source)
        self.assertGreaterEqual(source.count("assert_creation_editor_gated(device)"), 3)
        for marker in (
            'device.tap_until_visible(\n        "creation-stage-foundation"',
            'tap_first_enabled_prefix(device, "creation-foundation-metatype-")',
            'tap_first_enabled_prefix(device, "creation-foundation-nationality-")',
            'device.tap("creation-foundation-prepare-preview"',
            'device.wait("creation-foundation-preview-diff-"',
            'device.tap("creation-foundation-confirm"',
            'device.wait("creation-foundation-confirm-receipt"',
            'device.tap("creation-foundation-save"',
            'device.wait("creation-foundation-pending-draft"',
            '"foundationDraftSaveReloadAndProcessRestart": "pass"',
            '"foundationCharacterEffectsAppliedFalse": "pass"',
            '"foundationCompilationPending": "pass"',
            '"advancedEditorNeverExposedWhileCreatedFalse": "pass"',
        ):
            self.assertIn(marker, source)
        self.assertIn('device.shell("am", "force-stop", shared.PACKAGE)', source)
        self.assertIn('"rookTranscriptSurvivesProcessRestart": "pass"', source)
        self.assertNotIn('"profile": "tablet"', source)


if __name__ == "__main__":
    unittest.main()
