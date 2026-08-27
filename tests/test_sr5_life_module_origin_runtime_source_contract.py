import pathlib
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[1]
NATIVE = ROOT / "src/Chummer.Android/Native"


class Sr5LifeModuleOriginRuntimeSourceContractTests(unittest.TestCase):
    def test_production_authority_and_restart_runtime_are_registered(self):
        program = (ROOT / "src/Chummer.Android/MauiProgram.cs").read_text(encoding="utf-8")
        runtime = (NATIVE / "OriginDossierLifeModulePhoneRuntime.cs").read_text(encoding="utf-8")

        self.assertIn("AddSingleton<ILifeModuleDecisionAuthority>", program)
        self.assertIn("CharacterCreationFoundationLifeModuleDecisionAuthority", program)
        self.assertIn("AddSingleton<LifeModuleOriginDossierInteractionService>", program)
        self.assertIn("AddSingleton<OriginDossierLifeModulePhoneRuntime>", program)
        self.assertIn("IOriginDossierDraftTimelineStore", runtime)
        self.assertIn("_interaction.Restore(checkpoint)", runtime)
        self.assertIn("explicitlyConfirmed: true", runtime)
        self.assertIn("checkpoint.BoundSeedDigest", runtime)
        self.assertIn("_store.DeleteAsync", runtime)

    def test_entry_is_sr5_life_modules_only_and_never_home(self):
        coordinator = (NATIVE / "RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        build = (NATIVE / "BuildPage.cs").read_text(encoding="utf-8")
        home = (NATIVE / "HomePage.cs").read_text(encoding="utf-8")

        gate = coordinator[coordinator.index("CanOpenSr5LifeModuleOrigin"):]
        gate = gate[: gate.index("PrepareSr5LifeModuleOriginAsync")]
        self.assertIn("CharacterCreationBuildMethods.LifeModules", gate)
        self.assertIn('foundation.RulesetId, "sr5"', gate)
        self.assertIn("foundation.Binding.ContentRevision == State.ContentRevision", gate)
        self.assertIn("CharacterCreationWizardStepIds.LifeModules", build)
        self.assertIn("OpenSr5LifeModuleOriginAsync", build)
        self.assertIn('creation-stage-{Token(stage.StepId)}', build)
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


if __name__ == "__main__":
    unittest.main()
