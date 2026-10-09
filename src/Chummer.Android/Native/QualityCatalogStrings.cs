using System.Globalization;
using System.Resources;

namespace Chummer.Android.Native;

/// <summary>Display-only names bound to both the pinned source ID and neutral
/// name. Custom/renamed qualities keep their supplied name; IDs and rules never change.</summary>
public static class QualityCatalogStrings
{
    private static readonly ResourceManager Resources = new(
        "Chummer.Android.Resources.Localization.QualityCatalogStrings", typeof(QualityCatalogStrings).Assembly);

    public static string Name(Guid sourceId, string name)
    {
        string key = $"Quality.{sourceId:D}.Name";
        return string.Equals(Lookup(key, CultureInfo.InvariantCulture), name, StringComparison.Ordinal)
            ? Lookup(key, CultureInfo.CurrentUICulture) ?? name : name;
    }

    public static bool MatchesSearch(Guid sourceId, string name, string? followUp, string filter)
        => string.IsNullOrWhiteSpace(filter)
           || Name(sourceId, name).Contains(filter, StringComparison.CurrentCultureIgnoreCase)
           || name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
           || (followUp?.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ?? false);

    private static string? Lookup(string key, CultureInfo culture)
    {
        try { return Resources.GetString(key, culture) is { Length: > 0 } text ? text : null; }
        catch (MissingManifestResourceException) { return null; }
        catch (MissingSatelliteAssemblyException) { return null; }
    }
}
