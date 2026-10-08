using System.Globalization;
using System.Resources;

namespace Chummer.Android.Native;

/// <summary>
/// Resource-backed native phone copy. Callers provide an explicit English fallback so a missing
/// or damaged satellite assembly remains usable and can be detected by completeness tests.
/// </summary>
public static class PhoneStrings
{
    // Display only. Workspace selection/deletion must continue using the retained
    // typed identity, never this label (names and aliases need not be unique).
    internal static string RunnerName(string? name, string? alias)
    {
        name = name?.Trim();
        alias = alias?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return string.IsNullOrWhiteSpace(alias) ? Get("RunnerFallback", "Runner") : alias;
        if (string.IsNullOrWhiteSpace(alias)
            || string.Equals(alias, "Runner", StringComparison.OrdinalIgnoreCase)
            || string.Equals(alias, name, StringComparison.OrdinalIgnoreCase)) return name;
        return $"{alias} · {name}";
    }

    private static readonly ResourceManager ResourceManager = new(
        "Chummer.Android.Resources.Localization.PhoneStrings",
        typeof(PhoneStrings).Assembly);

    public static string Get(string key, string englishFallback, CultureInfo? culture = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(englishFallback);
        try
        {
            return ResourceManager.GetString(key, culture ?? CultureInfo.CurrentUICulture)
                   ?? englishFallback;
        }
        catch (MissingManifestResourceException)
        {
            // Platform-neutral compile gates intentionally omit Android resource satellites.
            return englishFallback;
        }
    }

    public static string Format(
        string key,
        string englishFallback,
        params object?[] arguments)
        => string.Format(
            CultureInfo.CurrentCulture,
            Get(key, englishFallback),
            arguments);
}
