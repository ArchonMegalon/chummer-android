using System.Globalization;
using System.Resources;

namespace Chummer.Android.Native;

/// <summary>
/// Resource-backed UI copy for the native SR5 creation Attribute, Skills, and Metatype
/// allocation surfaces. Core-projected identifiers, digests, and blockers deliberately
/// retain their exact values; translated labels and guidance are display-only, never admission logic.
/// </summary>
public static class CreationAllocationStrings
{
    public static string BasicsSummary => Get("Creation.BasicsSummary", "View your runner's rules and enabled sourcebooks. These settings cannot be changed here.");

    private static readonly ResourceManager Resources = new(
        "Chummer.Android.Resources.Localization.CreationAllocationStrings",
        typeof(CreationAllocationStrings).Assembly);

    public static string Get(string key, string englishFallback)
        => Get(key, englishFallback, CultureInfo.CurrentUICulture);

    public static string Get(string key, string englishFallback, CultureInfo culture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(englishFallback);
        ArgumentNullException.ThrowIfNull(culture);
        try
        {
            return Resources.GetString(key, culture) is { Length: > 0 } value
                ? value
                : englishFallback;
        }
        catch (MissingManifestResourceException)
        {
            return englishFallback;
        }
        catch (MissingSatelliteAssemblyException)
        {
            return englishFallback;
        }
    }

    public static string Format(string key, string englishFallback, params object?[] arguments)
        // App language and regional number/date formats are independent settings.
        => string.Format(CultureInfo.CurrentCulture, Get(key, englishFallback), arguments);

    public static string Format(
        CultureInfo culture,
        string key,
        string englishFallback,
        params object?[] arguments)
        => string.Format(culture, Get(key, englishFallback, culture), arguments);

    public static string AttributeName(string attributeId)
        => attributeId switch
        {
            "BOD" => Get("Attribute.BOD", "Body"),
            "AGI" => Get("Attribute.AGI", "Agility"),
            "REA" => Get("Attribute.REA", "Reaction"),
            "STR" => Get("Attribute.STR", "Strength"),
            "CHA" => Get("Attribute.CHA", "Charisma"),
            "INT" => Get("Attribute.INT", "Intuition"),
            "LOG" => Get("Attribute.LOG", "Logic"),
            "WIL" => Get("Attribute.WIL", "Willpower"),
            "EDG" => Get("Attribute.EDG", "Edge"),
            "MAG" => Get("Attribute.MAG", "Magic"),
            "RES" => Get("Attribute.RES", "Resonance"),
            "ESS" => Get("Attribute.ESS", "Essence"),
            "DEP" => Get("Attribute.DEP", "Depth"),
            _ => attributeId
        };

    // Exact blocker codes remain in the technical disclosure. Unknown codes never
    // become permission to spend points or a guessed rule explanation.
    public static string AttributeBlocker(string code) => code switch
    {
        "creation-attributes-special-not-enabled" or "creation-attributes-attribute-disabled"
            => Get("Attributes.NotEnabledByTalent", "Not enabled by this Talent"),
        "creation-attributes-essence-not-spendable"
            => Get("Attributes.EssenceNotSpendable", "Essence cannot be raised with attribute points or Karma here."),
        "creation-attributes-special-points-exceeded"
            => Get("Attributes.SpecialPointsExceeded", "Not enough special attribute points remain."),
        "creation-attributes-normal-points-exceeded"
            => Get("Attributes.NormalPointsExceeded", "Not enough normal attribute points remain."),
        "creation-attributes-global-karma-exceeded"
            => Get("Attributes.KarmaExceeded", "Not enough creation Karma remains."),
        "creation-attributes-maximum-count-exceeded"
            => Get("Attributes.MaximumCountExceeded", "Too many attributes are at their natural maximum. Lower another attribute first."),
        "creation-attributes-stale-workspace-revision" or "creation-attributes-stale-raw-character-xml-digest"
            or "creation-attributes-preview-digest-mismatch"
            => Get("AttributesPreview.Reopen", "This review is no longer current. Return to your runner and reopen Attributes to check the latest choices."),
        "creation-attributes-confirm-outcome-unknown"
            => Get("AttributesPreview.SaveUncertain", "We couldn't confirm whether your choices were saved. Reopen your runner and check Attributes before making another change. Don't repeat this save."),
        "creation-attributes-post-commit-refresh-required"
            => Get("AttributesPreview.SavedReopen", "Your attribute choices were saved, but this view couldn't refresh. Reopen your runner to see them. Don't save these choices again."),
        _ => Get("Attributes.ChangeUnavailable", "This change is unavailable with the current choices and limits. Technical details contain the exact reason.")
    };
}
