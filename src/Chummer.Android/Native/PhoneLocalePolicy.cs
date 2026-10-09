using System.Globalization;
using Microsoft.Maui.Storage;

namespace Chummer.Android.Native;

public sealed record PhoneLocaleSelection(
    string RequestedLocale,
    string EffectiveUiLocale,
    bool UsesEnglishFallback);

public sealed record PhoneLocalePreferences(string Language, string Region)
{
    public static PhoneLocalePreferences System { get; } = new(string.Empty, string.Empty);
}

public sealed record PhoneLocaleChoice(string Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Resolves the native phone UI locale before any page or resource is constructed. Rules/source,
/// Origin-authoring, and published-edition locales remain separate domain values.
/// </summary>
public static class PhoneLocalePolicy
{
    public const string EnglishLocale = "en-US";
    public const string GermanLocale = "de-DE";
    public const string SpanishLocale = "es-ES";
    public const string PreferencesKey = "phone-language-region-v1";
    public static CultureInfo SystemFormatCulture { get; private set; } = CultureInfo.CurrentCulture;

    public static PhoneLocalePreferences ReadPreferences(IPreferences preferences)
    {
        // One small value keeps language and format choices together. A corrupt/old value
        // must not stop the app from starting, or be interpreted as a domain/story locale.
        string value = preferences.Get(PreferencesKey, string.Empty);
        if (value.Length > 128) return PhoneLocalePreferences.System;
        string[] parts = value.Split('|');
        if (parts.Length != 3 || parts[0] != "1") return PhoneLocalePreferences.System;
        PhoneLocalePreferences selection = new(parts[1], parts[2]);
        return IsValid(selection) ? selection : PhoneLocalePreferences.System;
    }

    public static void SavePreferences(IPreferences preferences, PhoneLocalePreferences selection)
    {
        if (!IsValid(selection)) throw new ArgumentException("Unsupported phone locale selection.", nameof(selection));
        preferences.Set(PreferencesKey, $"1|{selection.Language}|{selection.Region}");
        // Apply on the next launch, not inside an async Save continuation. Replacing the
        // visual tree here would discard unsaved forms in other navigation stacks.
    }

    public static PhoneLocaleSelection InitializeFromPreferences(
        IPreferences preferences, CultureInfo? systemUiCulture = null, CultureInfo? systemCulture = null)
    {
        PhoneLocalePreferences saved = ReadPreferences(preferences);
        SystemFormatCulture = systemCulture ?? CultureInfo.CurrentCulture;
        CultureInfo ui = saved.Language.Length == 0
            ? systemUiCulture ?? CultureInfo.CurrentUICulture
            : CultureInfo.GetCultureInfo(saved.Language);
        CultureInfo formats = saved.Region.Length == 0
            ? SystemFormatCulture
            : CultureInfo.GetCultureInfo(saved.Region);
        PhoneLocaleSelection selection = InitializeFromSystemCulture(ui);
        CultureInfo.CurrentCulture = formats;
        CultureInfo.DefaultThreadCurrentCulture = formats;
        return selection;
    }

    public static PhoneLocaleChoice[] LanguageChoices() =>
    [
        new(string.Empty, PhoneStrings.Get("SettingsFollowPhone", "Use phone setting")),
        new("de", "Deutsch"), new("en", "English"), new("es", "Español")
    ];

    public static PhoneLocaleChoice[] RegionChoices()
    {
        string[] common = ["de-AT", "de-DE", "de-CH", "en-GB", "en-US", "es-ES", "es-MX"];
        return [new(string.Empty, PhoneStrings.Get("SettingsFollowPhone", "Use phone setting")),
            .. CultureInfo.GetCultures(CultureTypes.SpecificCultures)
                .Where(culture => culture.Name.Length > 0)
                .DistinctBy(culture => culture.Name)
                .OrderBy(culture =>
                {
                    int index = Array.IndexOf(common, culture.Name);
                    return index < 0 ? common.Length : index;
                })
                .ThenBy(culture => culture.NativeName, StringComparer.CurrentCulture)
                .Select(culture => new PhoneLocaleChoice(culture.Name, culture.NativeName))];
    }

    private static bool IsValid(PhoneLocalePreferences selection)
        => selection.Language is "" or "de" or "en" or "es"
           && selection.Region is not null
           && (selection.Region.Length == 0
               || CultureInfo.GetCultures(CultureTypes.SpecificCultures)
                   .Any(culture => string.Equals(culture.Name, selection.Region, StringComparison.Ordinal)));

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
