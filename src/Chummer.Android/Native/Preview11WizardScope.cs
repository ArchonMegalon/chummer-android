using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// Presentation-only boundary for the exact Preview.11 internal phone scope.
/// This catalog labels routes; it never grants runtime, persistence, or
/// publication authority.
/// </summary>
public static class Preview11WizardScope
{
    public static bool CoversCreationMethod(string buildMethod)
        => buildMethod is CharacterCreationBuildMethods.Priority
            or CharacterCreationBuildMethods.SumToTen;

    public static bool CoversCreationStage(string stepId)
        => string.Equals(
            stepId,
            CharacterCreationWizardStepIds.Resources,
            StringComparison.Ordinal);

    public static bool CoversCareerAction(string actionId)
        => actionId is Sr5CareerWizardActionIds.AdvanceActiveSkill
            or Sr5CareerWizardActionIds.BeforeRun
            or Sr5CareerWizardActionIds.Playtime
            or Sr5CareerWizardActionIds.ManageCalendarEntry;

    public static string MarkExperimental(string detail)
        => WizardStrings.Format(
            "Preview11.ExperimentalRoute",
            "{0} · Experimental — not covered by the current Preview authority.",
            detail);

    public static string ContainsExperimentalRoutes(string detail)
        => WizardStrings.Format(
            "Preview11.ContainsExperimentalRoutes",
            "{0} · Contains Experimental routes that are not covered by the current Preview authority.",
            detail);
}
