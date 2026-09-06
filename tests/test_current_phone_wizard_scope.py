from pathlib import Path
import xml.etree.ElementTree as ET
import unittest


ROOT = Path(__file__).resolve().parents[1]
NATIVE = ROOT / "src" / "Chummer.Android" / "Native"
SCOPE = NATIVE / "CurrentPhoneWizardScope.cs"
BUILD = NATIVE / "BuildPage.cs"
CAREER_PAGE = NATIVE / "Sr5CareerWizardPage.cs"
LOCALIZATION = ROOT / "src" / "Chummer.Android" / "Resources" / "Localization"
NATIVE_COMPILE_CHECK = (
    ROOT
    / "tests"
    / "Chummer.Android.Native.CompileCheck"
    / "Chummer.Android.Native.CompileCheck.csproj"
)


def resource(path: Path, key: str) -> str:
    root = ET.parse(path).getroot()
    values = {
        node.attrib["name"]: node.findtext("value", default="")
        for node in root.findall("data")
    }
    return values[key]


def route_block(source: str, automation_id: str) -> str:
    marker = f'automationId: "{automation_id}"'
    end = source.index(marker)
    start = source.rfind("NativeTheme.NavigationRow(", 0, end)
    assert start >= 0
    return source[start : end + len(marker)]


class CurrentPhoneWizardScopeTests(unittest.TestCase):
    def test_current_scope_catalog_is_exactly_the_seven_gated_flow_surface(self) -> None:
        source = SCOPE.read_text(encoding="utf-8")

        assert "CharacterCreationBuildMethods.Priority" in source
        assert "CharacterCreationBuildMethods.SumToTen" not in source
        assert "CharacterCreationBuildMethods.LifeModules" not in source
        assert "CharacterCreationWizardStepIds.Resources" in source
        for excluded_stage in (
            "Basics",
            "Foundation",
            "LifeModules",
            "Attributes",
            "Skills",
            "Qualities",
            "MagicResonance",
            "ContactsLifestyles",
            "IdentityStory",
        ):
            assert f"CharacterCreationWizardStepIds.{excluded_stage}" not in source

        covered_actions = {
            "AdvanceActiveSkill",
            "BeforeRun",
            "Playtime",
            "ManageCalendarEntry",
        }
        actual_actions = {
            line.split("Sr5CareerWizardActionIds.", 1)[1].strip().rstrip(";")
            for line in source.splitlines()
            if "Sr5CareerWizardActionIds." in line
        }
        assert actual_actions == covered_actions


    def test_every_visible_uncovered_creation_route_is_marked_without_being_hidden(self) -> None:
        source = BUILD.read_text(encoding="utf-8")
        stages = source[source.index("private void AddWizardStages(") : source.index("private void AddCompletionBlockers(")]
        next_steps = source[source.index("private void AddLegalNextSteps(") : source.index("private static string? ProjectionStageBlocker(")]
        method = source[source.index("private void AddCreationMethodRoute(") : source.index("private void AddFinalizationReviewAction(")]
        finalization = source[source.index("private void AddFinalizationReviewAction(") : source.index("private void AddCreationFinalizationStatus(")]

        assert "canOpen && !CurrentPhoneWizardScope.CoversCreationStage(stage.StepId)" in stages
        assert "detail = CurrentPhoneWizardScope.MarkExperimental(detail);" in stages
        assert "enabled: canOpen" in stages
        assert "canOpen && !CurrentPhoneWizardScope.CoversCreationStage(stepId)" in next_steps
        assert "detail = CurrentPhoneWizardScope.MarkExperimental(detail);" in next_steps
        assert "canOpen && !CurrentPhoneWizardScope.CoversCreationMethod(snapshot.BuildMethod)" in method
        assert "detail = CurrentPhoneWizardScope.MarkExperimental(detail);" in method
        assert 'CurrentPhoneWizardScope.MarkExperimental("Review and finish creation")' in finalization


    def test_every_visible_uncovered_career_route_is_marked_without_being_hidden(self) -> None:
        build = BUILD.read_text(encoding="utf-8")
        for automation_id in (
            "build-career-quality",
            "build-career-skill-group",
            "build-career-specialization",
            "build-career-commerce",
            "build-career-vehicle-workshop",
        ):
            block = route_block(build, automation_id)
            assert "CurrentPhoneWizardScope.MarkExperimental(" in block

        page = CAREER_PAGE.read_text(encoding="utf-8")
        assert "!CurrentPhoneWizardScope.CoversCareerAction(action.ActionId)" in page
        assert "detail = CurrentPhoneWizardScope.MarkExperimental(detail);" in page
        assert "CurrentPhoneWizardScope.ContainsExperimentalRoutes(familyDetail)" in page
        commerce = page[
            page.index("View commerceRoute = NativeTheme.NavigationRow(") :
            page.index("_body.Add(commerceRoute);")
        ]
        assert "CurrentPhoneWizardScope.MarkExperimental(" in commerce
        assert "enabled: canOpenCommerce" in commerce


    def test_experimental_route_marker_is_explicit_in_all_supported_languages(self) -> None:
        expected = {
            "WizardStrings.resx": "Experimental — not covered by the current Preview authority",
            "WizardStrings.de.resx": "Experimentell — nicht durch die aktuelle Preview-Autorität abgedeckt",
            "WizardStrings.es.resx": "Experimental — no cubierto por la autoridad de la vista previa actual",
        }
        for filename, phrase in expected.items():
            value = resource(LOCALIZATION / filename, "CurrentPhoneWizard.ExperimentalRoute")
            assert phrase in value
            assert "{0}" in value

    def test_native_interaction_gate_embeds_wizard_localizations(self) -> None:
        project = NATIVE_COMPILE_CHECK.read_text(encoding="utf-8")
        for filename in (
            "WizardStrings.resx",
            "WizardStrings.de.resx",
            "WizardStrings.es.resx",
        ):
            assert (
                f'EmbeddedResource Include="../../src/Chummer.Android/'
                f'Resources/Localization/{filename}"' in project
            )


if __name__ == "__main__":
    unittest.main()
