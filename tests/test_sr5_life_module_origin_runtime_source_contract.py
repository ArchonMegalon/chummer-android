import pathlib
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[1]
NATIVE = ROOT / "src/Chummer.Android/Native"


class Sr5LifeModuleOriginRuntimeSourceContractTests(unittest.TestCase):
    def test_production_authority_and_restart_runtime_are_registered(self):
        program = (ROOT / "src/Chummer.Android/MauiProgram.cs").read_text(encoding="utf-8")
        runtime = (NATIVE / "OriginDossierLifeModulePhoneRuntime.cs").read_text(encoding="utf-8")
        coordinator = (NATIVE / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")

        self.assertIn("AddSingleton<ILifeModuleDecisionAuthority>", program)
        self.assertIn("CharacterCreationFoundationLifeModuleDecisionAuthority", program)
        self.assertIn("AddSingleton<LifeModuleOriginDossierInteractionService>", program)
        self.assertIn("AddSingleton<OriginDossierLifeModulePhoneRuntime>", program)
        self.assertIn("IOriginDossierDraftTimelineStore", runtime)
        self.assertIn("_interaction.Restore(checkpoint)", runtime)
        self.assertIn("explicitlyConfirmed: true", runtime)
        self.assertIn("checkpoint.BoundSeedDigest", runtime)
        self.assertIn("_store.DeleteAsync", runtime)
        self.assertIn("BoundContentDigest: checkpoint.BoundContentDigest", runtime)
        self.assertIn("BoundSourceDigest: checkpoint.BoundSourceDigest", runtime)
        self.assertIn("BoundMechanicsSnapshotDigest: checkpoint.BoundMechanicsSnapshotDigest", runtime)
        self.assertIn("BindCurrentLifeModuleBudget(result)", coordinator)
        self.assertIn("foundation.LifeModuleBudget.IsExact", coordinator)
        self.assertIn("foundation.Binding.RawCharacterXmlDigest", coordinator)
        self.assertIn("foundation.Binding.SourceDigest", coordinator)

    def test_entry_is_sr5_life_modules_only_and_never_home(self):
        coordinator = (NATIVE / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        build = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")
        copy = (NATIVE / "AndroidSurfaceStrings.cs").read_text(encoding="utf-8")
        home = (NATIVE / "HomePage.cs").read_text(encoding="utf-8")
        origin_entry = build[build.index("private async Task OpenSr5LifeModuleOriginAsync"):]
        origin_entry = origin_entry[: origin_entry.index("private Task OpenCreationPrerequisiteAsync")]

        gate = coordinator[coordinator.index("CanOpenSr5LifeModuleOrigin"):]
        gate = gate[: gate.index("PrepareSr5LifeModuleOriginAsync")]
        self.assertIn("CharacterCreationBuildMethods.LifeModules", gate)
        self.assertIn('foundation.RulesetId, "sr5"', gate)
        self.assertIn("foundation.Binding.ContentRevision == State.ContentRevision", gate)
        self.assertIn("CharacterCreationWizardStepIds.LifeModules", build)
        self.assertIn("OpenSr5LifeModuleOriginAsync", build)
        self.assertIn('creation-stage-{Token(stage.StepId)}', build)
        self.assertEqual(3, origin_entry.count("await DisplayAlertAsync("))
        self.assertNotIn("await DisplayAlert(", origin_entry)
        for key in (
            "Origin.UnavailableTitle",
            "Origin.UnavailableDetail",
            "Origin.PreviewUnavailableTitle",
            "Origin.PreviewUnavailableDetail",
            "Origin.DecisionNotSavedTitle",
            "Origin.DecisionNotSavedDetail",
        ):
            self.assertIn(f'copy["{key}"]', origin_entry)
            self.assertEqual(3, copy.count(f'("{key}",'))
        self.assertNotIn("OpenSr5LifeModuleOriginAsync", home)
        self.assertNotIn("origin-life-decision", home)

    def test_phone_confirmation_never_calculates_or_mutates_mechanics(self):
        page = (NATIVE / "OriginDossierLifeModuleDecisionPage.cs").read_text(encoding="utf-8")
        runtime = (NATIVE / "OriginDossierLifeModulePhoneRuntime.cs").read_text(encoding="utf-8")

        self.assertIn('AutomationId = "origin-life-confirm"', page)
        self.assertIn("await _confirmChoice(selectedChoiceId, previewDigest)", page)
        self.assertIn("_interaction.Confirm(", runtime)
        self.assertNotIn("IWorkspaceStore", runtime)
        self.assertNotIn("ReplaceWorkspace", runtime)

    def test_phone_renders_exact_core_budget_and_source_anchors(self):
        page = (NATIVE / "OriginDossierLifeModuleDecisionPage.cs").read_text(encoding="utf-8")
        copy = (NATIVE / "AndroidSurfaceStrings.cs").read_text(encoding="utf-8")

        for marker in (
            "CharacterCreationBudgetIds.LifeModules",
            "result.LifeModuleBudget.IsExact",
            "result.LifeModuleBudget.Blockers.Count == 0",
            'budgetCard.AutomationId = "origin-life-budget"',
            '"origin-life-budget-total"',
            '"origin-life-budget-used"',
            '"origin-life-budget-remaining"',
            "choice.SourceAnchorIds",
            'anchors.AutomationId = $"origin-life-choice-anchors-{choiceIndex}"',
            "prepared.BoundMechanicsSnapshotDigest",
        ):
            self.assertIn(marker, page)
        for key in (
            "Origin.Budget",
            "Origin.BudgetTotal",
            "Origin.BudgetUsed",
            "Origin.BudgetRemaining",
            "Origin.BudgetSemantic",
            "Origin.SourceAnchors",
        ):
            self.assertEqual(3, copy.count(f'("{key}",'))


if __name__ == "__main__":
    unittest.main()
