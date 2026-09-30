using System.Globalization;
using System.Resources;

namespace Chummer.Android.Native;

/// <summary>
/// Resource-backed copy for the native Contacts, Lifestyles, Qualities, and
/// Magic/Resonance creation flows. Rules values and authority diagnostics never pass through it.
/// </summary>
public static class CreationFlowStrings
{
    // Presentation only: exact known blockers get an actionable explanation.
    // Unknown codes are retained verbatim, never converted into readiness.
    internal static string FinalizationBlocker(string code) => code switch
    {
        "creation-finalization-prerequisite-draft-required"
            or "creation-attributes-prerequisite-draft-required"
            or "creation-magic-resonance-prerequisite-draft-required"
            or "creation-qualities-prerequisite-draft-required"
            or "creation-resources-prerequisite-draft-required"
            => Get("Finalization.ReviewMethod", "Open Build method, review your priorities and metatype, and save your choices."),
        "creation-finalization-attributes-draft-required"
            or "creation-magic-resonance-attributes-draft-required"
            or "creation-qualities-attributes-draft-required"
            or "creation-skills-attributes-draft-required"
            => Get("Finalization.ReviewAttributes", "Open Attributes and review and save your allocation."),
        "creation-finalization-skills-draft-required"
            => Get("Finalization.ReviewSkills", "Open Skills and review and save your skill and language choices."),
        "creation-finalization-qualities-draft-required"
            => Get("Finalization.ReviewQualities", "Open Qualities and review and save your choices, even if you choose none."),
        "creation-finalization-magic-resonance-draft-required"
            => Get("Finalization.ReviewMagic", "Open Magic / Resonance and review and save the choices required for your talent."),
        "creation-finalization-resources-draft-required" or "creation-gear-resources-draft-required"
            => Get("Finalization.ReviewResources", "Open Resources and review and save your funding before choosing equipment."),
        "creation-finalization-gear-draft-required"
            => Get("Finalization.ReviewGear", "Open Gear and review and save your equipment choices."),
        "creation-skills-prerequisite-source-drift"
            => Get("Finalization.ReviewSkillSources", "Open Skills and review your choices against the current build method and enabled books."),
        _ => code
    };

    private static readonly ResourceManager Resources = new(
        "Chummer.Android.Resources.Localization.CreationFlowStrings",
        typeof(CreationFlowStrings).Assembly);

    public static string Get(string key, string fallback)
        => Get(key, fallback, CultureInfo.CurrentUICulture);

    public static string Get(string key, string fallback, CultureInfo culture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(culture);
        try
        {
            return Resources.GetString(key, culture) is { Length: > 0 } value
                ? value
                : fallback;
        }
        catch (MissingManifestResourceException)
        {
            return fallback;
        }
    }

    public static string Format(string key, string fallback, params object?[] arguments)
        => Format(CultureInfo.CurrentUICulture, key, fallback, arguments);

    public static string Format(
        CultureInfo culture,
        string key,
        string fallback,
        params object?[] arguments)
        => string.Format(culture, Get(key, fallback, culture), arguments);
}
