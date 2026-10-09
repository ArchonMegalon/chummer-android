using System.Globalization;
using System.Resources;
using System.Security.Cryptography;
using System.Text;

namespace Chummer.Android.Native;

/// <summary>Display-only names from pinned Core translations. Never a selection key
/// or rules input. Custom/renamed/missing entries retain their exact supplied label.</summary>
public static class SkillCatalogStrings
{
    private static readonly ResourceManager Resources = new(
        "Chummer.Android.Resources.Localization.SkillCatalogStrings", typeof(SkillCatalogStrings).Assembly);

    public static string SkillName(string kind, string sourceId, string name)
        => Display($"Skill.{kind}.{sourceId}.Name", name);

    public static string GroupName(string name) => Display("Group." + Token(name), name);
    public static string CategoryName(string name) => Display("Category." + Token(name), name);

    public static string SpecializationName(string kind, string sourceId, string skillName, string name)
    {
        string prefix = $"Skill.{kind}.{sourceId}";
        // An override can retain a canonical ID but rename the skill. Never borrow
        // specializations from a different meaning merely because its ID matches.
        return string.Equals(Lookup(prefix + ".Name", CultureInfo.InvariantCulture), skillName, StringComparison.Ordinal)
            ? Display(prefix + ".Spec." + Token(name), name) : name;
    }

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
