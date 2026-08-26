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
        authority = (NATIVE / "CreationFoundationPhoneAuthority.cs").read_text(encoding="utf-8")
        dashboard = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")

        for marker in (
            "Coordinator.LoadCreationFoundation()",
            "state.MetatypeOptions",
            "AddNationalitySelection(state)",
            "new CreationNationalityPage(Coordinator, _phoneDraft)",
            '"creation-foundation-open-nationality"',
            "nationality.FollowUps.Concat(version?.FollowUps ?? [])",
            'string.Equals(prompt.InputKind, "select"',
            'string.Equals(prompt.InputKind, "single-select"',
            "option.SourceValue",
            "Unsupported follow-up kind:",
            "new CreationMetatypePage(Coordinator, _phoneDraft)",
            '"creation-foundation-open-metatype"',
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

        for marker in (
            "IsMetatypeEvaluationCandidate(",
            "HasExactCandidateIdentityCostAndSource(",
            "HasOnlyEligibilityAuthorityBlocker(",
            "CharacterCreationFoundationBlockers.CharacterEligibilityAuthorityRequired",
            "HasOnlyTypedMetatypeRequirements(",
            "requirement.RequiresCharacterAuthority",
            'string.Equals(requirement.Operator, "oneof"',
            'string.Equals(requirement.SubjectKind, "metatype"',
            "selectedMetatype.Label",
        ):
            self.assertIn(marker, authority)

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

    def test_phone_metatype_deep_navigation_is_typed_exact_and_non_writing(self) -> None:
        draft = (NATIVE / "CreationFoundationPhoneDraft.cs").read_text(encoding="utf-8")
        options = (NATIVE / "CreationMetatypePage.cs").read_text(encoding="utf-8")
        preview = (NATIVE / "CreationMetatypePreviewPage.cs").read_text(encoding="utf-8")
        foundation = (NATIVE / "CreationFoundationPage.cs").read_text(encoding="utf-8")

        for marker in (
            "ConfirmedMetatypeOptionId",
            "BindingEquals(_binding, state.Binding)",
            "left.WorkspaceId.Equals(right.WorkspaceId)",
            "left.ContentRevision == right.ContentRevision",
            "left.SavedRevision == right.SavedRevision",
            "left.RawCharacterXmlDigest",
            "left.SourceDigest",
            "left.EnabledSources.SequenceEqual",
            "state.FoundationSnapshotDigest",
            "ResolveUniqueEnabledOption",
            "ResolveUniqueOption",
            "option.OptionId",
            "matches.Length == 1",
            "pending.RequestedMetatype",
            "pending.CharacterEffectsApplied",
            "pending.WorkspaceId.Equals(state.Binding.WorkspaceId)",
            "pending.SourceDigest",
        ):
            self.assertIn(marker, draft)

        for marker in (
            'AutomationId = "creation-metatype-page"',
            "Coordinator.LoadCreationFoundation()",
            "Coordinator.State.Profile?.Created != false",
            "_draft.Matches(state)",
            "state.MetatypeOptions",
            "option.OptionId",
            "option.IsEnabled",
            "option.DisableReasonKey",
            "option.DisableReasonArguments",
            "option.Costs",
            "cost.BudgetId",
            "option.SourceId",
            "option.SourcePage",
            "option.SourceAnchorIds",
            "budget.Remaining",
            "budget.IsExact",
            "new CreationMetatypePreviewPage(",
            '"creation-metatype-option-',
        ):
            self.assertIn(marker, options)

        for marker in (
            'AutomationId = "creation-metatype-preview-page"',
            "_draft.ResolveCandidate(state, _candidateOptionId)",
            "state.AuthorityBlockers",
            "state.LifeModuleBudget.Blockers",
            "state.LifeModuleBudget.IsExact",
            "option.Consequences",
            "consequence.SourceAnchorIds",
            "option.SourceAnchorIds",
            "option.DisableReasonArguments",
            'NativeTheme.PrimaryButton("Use this metatype")',
            'AutomationId = "creation-metatype-confirm"',
            "_draft.TryConfirmMetatype(state, _candidateOptionId)",
            "Navigation.PopAsync(animated: false)",
            "Navigation.NavigationStack.LastOrDefault() is CreationMetatypePage",
            "No character data is written here",
        ):
            self.assertIn(marker, preview)

        self.assertIn("_phoneDraft.Bind(state)", foundation)
        self.assertIn("_phoneDraft.ResolveConfirmedMetatype(state)", foundation)
        self.assertIn("metatype.Label", foundation)
        combined = draft + options + preview
        for forbidden in (
            '"Human"',
            '"Elf"',
            "Picker",
            "Coordinator.PrepareCreationFoundation",
            "Coordinator.ConfirmCreationFoundationAsync",
            "SaveAsync(",
            "System.Xml",
            "XmlDocument",
            "NativeCommandPage",
            "AddSectionActions",
            "AddQuickActions",
        ):
            self.assertNotIn(forbidden, combined)

    def test_phone_nationality_deep_navigation_is_typed_exact_and_non_writing(self) -> None:
        draft = (NATIVE / "CreationFoundationPhoneDraft.cs").read_text(encoding="utf-8")
        authority = (NATIVE / "CreationFoundationPhoneAuthority.cs").read_text(encoding="utf-8")
        options = (NATIVE / "CreationNationalityPage.cs").read_text(encoding="utf-8")
        preview = (NATIVE / "CreationNationalityPreviewPage.cs").read_text(encoding="utf-8")
        foundation = (NATIVE / "CreationFoundationPage.cs").read_text(encoding="utf-8")

        for marker in (
            "ConfirmedNationalityModuleId",
            "ConfirmedNationalityVersionId",
            "ResolveUniqueModule",
            "ResolveUniqueVersion",
            "TryConfirmNationality",
            "ResolvePendingNationalitySelection",
            "ResolvePendingFollowUpValues",
            "pending.Selection.ModuleId",
            "pending.Selection.VersionId",
            "pending.RequirementEvaluations.All",
            "!requirement.RequiresCharacterAuthority || requirement.IsMet",
            "option.SourceValue",
        ):
            self.assertIn(marker, draft)

        for marker in (
            "CanOpenModule",
            "CanReviewSelection",
            "state.MetatypeOptions.Count",
            "selectedMetatype.Label",
            "module.StageOrder != LifeModuleJourneyStageOrders.Nationality",
            "module.StageId",
            "module.CanRepeat",
            "module.KarmaIsExact",
            "version.KarmaIsExact",
            "module.SourceAnchorIds",
            "version.SourceAnchorIds",
            "module.AuthorityBlockers",
            "version?.AuthorityBlockers",
            "HasOnlyTypedMetatypeRequirements",
            "AcceptedValues.Contains",
        ):
            self.assertIn(marker, authority)

        for marker in (
            'AutomationId = "creation-nationality-page"',
            'AutomationId = "creation-nationality-version-page"',
            'AutomationId = "creation-nationality-version-budget"',
            "Coordinator.LoadCreationFoundation()",
            "_draft.Matches(state)",
            "_draft.ResolveConfirmedMetatype(state)",
            "state.NationalityOptions",
            "module.ModuleId",
            "version.VersionId",
            "module.KarmaCost",
            "version.KarmaCost",
            "module.AuthorityBlockers",
            "version.AuthorityBlockers",
            "module.SourceAnchorIds",
            "version.SourceAnchorIds",
            "budget.Remaining",
            "CreationNationalityPreviewPage",
            '"creation-nationality-option-',
            '"creation-nationality-version-option-',
        ):
            self.assertIn(marker, options)

        for marker in (
            'AutomationId = "creation-nationality-preview-page"',
            "CreationFoundationPhoneAuthority.ResolveUniqueModule",
            "CreationFoundationPhoneAuthority.ResolveUniqueVersion",
            "CreationFoundationPhoneAuthority.CanReviewSelection",
            "module.IsEnabled",
            "version.IsEnabled",
            "module.Requirements.Concat",
            "module.Effects.Concat",
            "module.FollowUps.Concat",
            "requirement.RequirementId",
            "requirement.Operator",
            "requirement.SubjectKind",
            "requirement.AcceptedValues",
            "requirement.DisableReasonArguments",
            "effect.EffectId",
            "effect.SourceAnchorIds",
            'NativeTheme.PrimaryButton("Use this Nationality")',
            'AutomationId = "creation-nationality-confirm"',
            "_draft.TryConfirmNationality(state, _moduleId, _versionId)",
            "Navigation.PopAsync(animated: false)",
            "CreationNationalityPage",
            "CreationNationalityVersionPage",
            "No character data is written here",
        ):
            self.assertIn(marker, preview)

        self.assertIn("_phoneDraft.ResolveConfirmedNationality(state)", foundation)
        self.assertIn("_phoneDraft.ResolveConfirmedNationalityVersion(state)", foundation)
        self.assertIn("SelectedNationalityVersion(state)?.VersionId", foundation)
        combined = draft + authority + options + preview
        for forbidden in (
            "Picker",
            "SelectedIndex = 0",
            "Coordinator.PrepareCreationFoundation",
            "Coordinator.ConfirmCreationFoundationAsync",
            "ApplyAttributeEditAsync",
            "AttributeEditRequest",
            "SaveAsync(",
            "System.Xml",
            "XmlDocument",
            '"Human"',
            '"Elf"',
        ):
            self.assertNotIn(forbidden, combined)

    def test_uncreated_build_is_gated_before_exhaustive_editor(self) -> None:
        source = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")
        start = source.index("if (Coordinator.State.Profile.Created == false)")
        end = source.index('Title = "Sheet";', start)
        creation_branch = source[start:end]

        self.assertNotIn("SetExhaustiveActionsVisible", source)
        self.assertIn("AddCreationWizardDashboard();", creation_branch)
        self.assertIn("return;", creation_branch)
        for forbidden in ("AddDossier();", "AddBuildAreas();"):
            self.assertNotIn(forbidden, creation_branch)
        self.assertNotIn("AddTools", source)

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
        self.assertNotIn("new RookConversationPage", source)
        self.assertNotIn('"creation-wizard-rook"', source)

    def test_attributes_use_dedicated_creation_authority_not_post_create_editor(self) -> None:
        dashboard = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")
        editor = (NATIVE / "AttributeEditPage.cs").read_text(encoding="utf-8")
        creation = (NATIVE / "CreationAttributesPage.cs").read_text(encoding="utf-8")

        self.assertIn("Coordinator.LoadCreationAttributes", dashboard)
        self.assertIn("CreationAttributesPhoneAuthority.IsReady", dashboard)
        self.assertIn("new CreationAttributesPage(Coordinator)", dashboard)
        self.assertIn("OpenCreationAttributesAsync", dashboard)
        self.assertIn("AttributeEditRequest path must", dashboard)
        self.assertIn("AttributeEditRequest", editor)
        self.assertIn("ApplyAttributeEditAsync", editor)
        self.assertNotIn("AttributeEditRequest", creation)
        self.assertNotIn("ApplyAttributeEditAsync", creation)

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
        self.assertIn("CreateCharacterActionId", route)
        self.assertIn("CompleteNewCharacterWorkflowActionId", route)
        self.assertIn("_coordinator.State.WorkspaceId is not null", route)
        self.assertIn("_coordinator.State.Profile?.Created == false", route)
        self.assertIn("UsesTabletComposition: false", route)
        self.assertIn("await shell.GoToAsync(PhoneShellRoutes.RunnerAbsolute)", route)
        self.assertLess(
            route.index("await CloseCoreAsync(updatePresenter: false)"),
            route.index("await shell.GoToAsync(PhoneShellRoutes.RunnerAbsolute)"),
        )

    def test_api36_driver_is_scripted_but_not_executed_by_unit_tests(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertIn('"profile": "phone"', source)
        self.assertIn('"status": "scripted_not_executed"', source)
        self.assertIn("shared.open_creation_dashboard(", source)
        self.assertNotIn('device.wait("creation-wizard-dashboard"', source)
        self.assertNotIn('device.tap("Continue building"', source)
        self.assertNotIn('device.tap("creation-wizard-rook"', source)
        self.assertNotIn('device.set_text("rook-question"', source)
        self.assertNotIn('device.tap("rook-send-question"', source)
        self.assertIn("def assert_creation_editor_gated", source)
        self.assertIn('"build-career-create-expense"', source)
        self.assertGreaterEqual(source.count("assert_creation_editor_gated(device)"), 3)
        for marker in (
            'device.tap_until_visible(\n        "creation-stage-foundation"',
            'device.tap("creation-foundation-open-metatype"',
            'tap_first_enabled_prefix(device, "creation-metatype-option-")',
            'device.wait("creation-metatype-preview-page"',
            'device.tap("creation-metatype-confirm"',
            'device.tap("creation-foundation-open-nationality"',
            'device.wait("creation-nationality-page"',
            'tap_first_enabled_prefix(device, "creation-nationality-option-")',
            '"creation-nationality-version-page"',
            '"creation-nationality-preview-page"',
            'tap_first_enabled_prefix(\n            device,\n            "creation-nationality-version-option-"',
            'device.tap("creation-nationality-confirm"',
            'Back navigation did not restore the confirmed Nationality selection',
            'Pending Foundation draft did not resume its typed Nationality IDs',
            'Process restart did not resume the typed Nationality IDs',
            'device.tap("creation-foundation-prepare-preview"',
            'device.wait("creation-foundation-preview-diff-"',
            'device.tap("creation-foundation-confirm"',
            'device.wait("creation-foundation-confirm-receipt"',
            'device.tap("creation-foundation-save"',
            'device.wait("creation-foundation-pending-draft"',
            '"foundationDraftSaveReloadAndProcessRestart": "pass"',
            '"foundationMetatypeDeepNavigation": "pass"',
            '"foundationMetatypeBackRestoration": "pass"',
            '"foundationNationalityDeepNavigation": "pass"',
            '"foundationNationalityExplicitDraftConfirm": "pass"',
            '"foundationNationalityBackRestoration": "pass"',
            '"foundationNationalityPendingDraftResume": "pass"',
            '"foundationCharacterEffectsAppliedFalse": "pass"',
            '"foundationCompilationPending": "pass"',
            '"advancedEditorNeverExposedWhileCreatedFalse": "pass"',
        ):
            self.assertIn(marker, source)
        self.assertIn('device.shell("am", "force-stop", shared.PACKAGE)', source)
        self.assertIn('"rookLaunchPostponedAndAbsent": "pass"', source)
        self.assertNotIn('"profile": "tablet"', source)


if __name__ == "__main__":
    unittest.main()
