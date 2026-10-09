using System.Globalization;
using System.Resources;
using System.Security.Cryptography;
using System.Text;

namespace Chummer.Android.Native;

/// <summary>Display-only pinned catalog names, never a rules or selection input.
/// Renamed/custom/missing entries retain their exact supplied label.</summary>
public static class MagicCatalogStrings
{
    private static readonly ResourceManager Resources = new(
        "Chummer.Android.Resources.Localization.MagicCatalogStrings", typeof(MagicCatalogStrings).Assembly);

    public static string OptionName(string kind, string sourceId, string name)
        => Display($"Option.{kind}.{sourceId}.Name", name);

    public static string CategoryName(string kind, string name) => Display($"Category.{kind}." + Token(name), name);
    public static string TalentName(string name) => Display("Talent." + Token(name), name);
    public static string MetatypeName(string name) => Display("Metatype." + Token(name), name);
    public static string MetatypeCategory(string name) => Display("MetatypeCategory." + Token(name), name);

    public static bool MatchesSearch(string kind, string sourceId, string name, string sourceBook, string filter)
        => string.IsNullOrEmpty(filter)
           || OptionName(kind, sourceId, name).Contains(filter, StringComparison.CurrentCultureIgnoreCase)
           || name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
           || sourceBook.Contains(filter, StringComparison.CurrentCultureIgnoreCase);

    private static string Display(string key, string original)
        => string.Equals(Lookup(key, CultureInfo.InvariantCulture), original, StringComparison.Ordinal)
            ? Lookup(key, CultureInfo.CurrentUICulture) ?? original : original;

    private static string? Lookup(string key, CultureInfo culture)
    {
        try { return Resources.GetString(key, culture) is { Length: > 0 } text ? text : null; }
        catch (MissingManifestResourceException) { return null; }
        catch (MissingSatelliteAssemblyException) { return null; }
    }

    private static string Token(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
