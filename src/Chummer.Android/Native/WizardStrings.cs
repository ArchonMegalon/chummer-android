using System.Globalization;
using System.Resources;

namespace Chummer.Android.Native;

/// <summary>
/// Resource-backed copy used only by the native Priority and SR5 Career wizard surfaces.
/// CultureInfo/ResourceManager provide the regional fallback chain (for example de-AT to de).
/// Rule-authoritative values and blocker identifiers deliberately remain outside this layer.
/// </summary>
public static class WizardStrings
{
    private static readonly ResourceManager Resources = new(
        "Chummer.Android.Resources.Localization.WizardStrings",
        typeof(WizardStrings).Assembly);

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
        => string.Format(
            CultureInfo.CurrentUICulture,
            Get(key, fallback, CultureInfo.CurrentUICulture),
            arguments);

    public static string Format(
        CultureInfo culture,
        string key,
        string fallback,
        params object?[] arguments)
        => string.Format(culture, Get(key, fallback, culture), arguments);

    public static string PriorityCategory(string categoryId, string fallback)
        => Get($"Priority.Category.{ResourceToken(categoryId)}", fallback);

    public static string PriorityHeritageKind(string kind, string fallback)
        => Get($"Priority.HeritageKind.{ResourceToken(kind)}", fallback);

    public static string CareerFamilyTitle(string familyId, string fallback)
        => Get($"Career.Family.{ResourceToken(familyId)}.Title", fallback);

    public static string CareerFamilyDetail(string familyId, string fallback)
        => Get($"Career.Family.{ResourceToken(familyId)}.Detail", fallback);

    public static string CareerActionTitle(string actionId, string fallback)
        => Get($"Career.Action.{ResourceToken(actionId)}.Title", fallback);

    public static string CareerActionDetail(string actionId, string fallback)
        => Get($"Career.Action.{ResourceToken(actionId)}.Detail", fallback);

    private static string ResourceToken(string value)
        => value.Trim().ToLowerInvariant();
}
