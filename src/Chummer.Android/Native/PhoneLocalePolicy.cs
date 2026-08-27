using System.Globalization;

namespace Chummer.Android.Native;

public sealed record PhoneLocaleSelection(
    string RequestedLocale,
    string EffectiveUiLocale,
    bool UsesEnglishFallback);

/// <summary>
/// Resolves the native phone UI locale before any page or resource is constructed. Rules/source,
/// Origin-authoring, and published-edition locales remain separate domain values.
/// </summary>
public static class PhoneLocalePolicy
{
    public const string EnglishLocale = "en-US";
    public const string GermanLocale = "de-DE";
    public const string SpanishLocale = "es-ES";

    public static PhoneLocaleSelection Resolve(CultureInfo? systemUiCulture)
    {
        string requested = CanonicalName(systemUiCulture);
        string language = systemUiCulture?.TwoLetterISOLanguageName.ToLowerInvariant() ?? string.Empty;
        return language switch
        {
            "de" => new(requested, requested, false),
            "en" => new(requested, requested, false),
            "es" => new(requested, requested, false),
            _ => new(requested, EnglishLocale, true)
        };
    }

    public static PhoneLocaleSelection InitializeFromSystemCulture(CultureInfo? systemUiCulture = null)
    {
        PhoneLocaleSelection selection = Resolve(systemUiCulture ?? CultureInfo.CurrentUICulture);
        CultureInfo effective = CultureInfo.GetCultureInfo(selection.EffectiveUiLocale);
        CultureInfo.CurrentUICulture = effective;
        CultureInfo.DefaultThreadCurrentUICulture = effective;
        return selection;
    }

    private static string CanonicalName(CultureInfo? culture)
        => culture is null || culture.Name.Length == 0
            ? EnglishLocale
            : CultureInfo.GetCultureInfo(culture.Name.Replace('_', '-')).Name;
}
