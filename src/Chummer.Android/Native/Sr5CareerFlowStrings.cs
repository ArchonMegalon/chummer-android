using System.Globalization;
using System.Resources;

namespace Chummer.Android.Native;

/// <summary>
/// Resource-backed product copy for the typed SR5 Career transaction flows.
/// The English source phrase is also the stable resource key so callers can
/// localize presentation-owned copy without translating Core-projected names,
/// blockers, IDs, digests, or receipt values.
/// </summary>
public static class Sr5CareerFlowStrings
{
    private static readonly ResourceManager Resources = new(
        "Chummer.Android.Resources.Localization.Sr5CareerFlowStrings",
        typeof(Sr5CareerFlowStrings).Assembly);

    public static string Text(string englishSource)
        => Text(englishSource, CultureInfo.CurrentUICulture);

    public static string Text(string englishSource, CultureInfo culture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(englishSource);
        ArgumentNullException.ThrowIfNull(culture);
        try
        {
            return Resources.GetString(englishSource, culture) is { Length: > 0 } value
                ? value
                : englishSource;
        }
        catch (MissingManifestResourceException)
        {
            return englishSource;
        }
    }

    public static string Format(string englishTemplate, params object?[] arguments)
        => string.Format(
            CultureInfo.CurrentUICulture,
            Text(englishTemplate, CultureInfo.CurrentUICulture),
            arguments);

    public static string Format(
        CultureInfo culture,
        string englishTemplate,
        params object?[] arguments)
        => string.Format(culture, Text(englishTemplate, culture), arguments);
}
