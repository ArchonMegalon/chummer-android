using System.Globalization;
using System.Resources;

namespace Chummer.Android.Native;

/// <summary>
/// Resource-backed copy for the native Contacts, Lifestyles, Qualities, and
/// Magic/Resonance creation flows. Rules values and authority diagnostics never pass through it.
/// </summary>
public static class CreationFlowStrings
{
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
