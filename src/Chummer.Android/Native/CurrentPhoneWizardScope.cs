using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// Presentation-only boundary for the current internal phone-wizard scope.
/// This catalog labels routes; it never grants runtime, persistence, or
/// publication authority.
/// </summary>
public static class CurrentPhoneWizardScope
{
    public static bool CoversCreationMethod(string buildMethod)
        => buildMethod is CharacterCreationBuildMethods.Priority;

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
            "CurrentPhoneWizard.ExperimentalRoute",
            "{0} · Experimental — not covered by the current Preview authority.",
            detail);

    public static string ContainsExperimentalRoutes(string detail)
        => WizardStrings.Format(
            "CurrentPhoneWizard.ContainsExperimentalRoutes",
            "{0} · Contains Experimental routes that are not covered by the current Preview authority.",
            detail);
}
