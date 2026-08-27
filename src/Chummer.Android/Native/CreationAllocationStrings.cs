using System.Globalization;
using System.Resources;

namespace Chummer.Android.Native;

/// <summary>
/// Resource-backed UI copy for the native SR5 creation Attribute, Skills, and Metatype
/// allocation surfaces. Core-projected labels, identifiers, digests, and blockers deliberately
/// remain outside this layer.
/// </summary>
public static class CreationAllocationStrings
{
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
        => Format(CultureInfo.CurrentUICulture, key, englishFallback, arguments);

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
}
