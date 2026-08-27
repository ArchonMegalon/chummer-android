using System.Globalization;
using Chummer.Android.Native;

internal static class Program
{
    private static void Main()
    {
        RegionalCulturesRemainRegionalAndSupported();
        UnsupportedCulturesFailClosedToEnglishUi();
        ResourcesResolveInAllSupportedLanguages();
        InitializationDoesNotChangeFormattingCulture();
        Console.WriteLine("Phone localization tests passed: 4");
    }

    private static void RegionalCulturesRemainRegionalAndSupported()
    {
        AssertSelection("de-AT", "de-AT", fallback: false);
        AssertSelection("en-GB", "en-GB", fallback: false);
        AssertSelection("es-MX", "es-MX", fallback: false);
        AssertSelection("de-DE", "de-DE", fallback: false);
        AssertSelection("en-US", "en-US", fallback: false);
        AssertSelection("es-ES", "es-ES", fallback: false);
    }

    private static void UnsupportedCulturesFailClosedToEnglishUi()
    {
        AssertSelection("fr-FR", PhoneLocalePolicy.EnglishLocale, fallback: true);
        AssertSelection("ja-JP", PhoneLocalePolicy.EnglishLocale, fallback: true);
    }

    private static void ResourcesResolveInAllSupportedLanguages()
    {
        Require(PhoneStrings.Get("ShellStories", "fallback", CultureInfo.GetCultureInfo("en-GB")) == "Stories");
        Require(PhoneStrings.Get("ShellStories", "fallback", CultureInfo.GetCultureInfo("de-AT")) == "Geschichten");
        Require(PhoneStrings.Get("ShellStories", "fallback", CultureInfo.GetCultureInfo("es-MX")) == "Historias");
        Require(PhoneStrings.Get("MissingKey", "safe fallback", CultureInfo.GetCultureInfo("de-AT")) == "safe fallback");
    }

    private static void InitializationDoesNotChangeFormattingCulture()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo? originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-AT");
            PhoneLocaleSelection selection =
                PhoneLocalePolicy.InitializeFromSystemCulture(CultureInfo.GetCultureInfo("es-MX"));
            Require(selection.EffectiveUiLocale == "es-MX");
            Require(CultureInfo.CurrentUICulture.Name == "es-MX");
            Require(CultureInfo.CurrentCulture.Name == "de-AT");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
            CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUiCulture;
        }
    }

    private static void AssertSelection(string requested, string effective, bool fallback)
    {
        PhoneLocaleSelection selection =
            PhoneLocalePolicy.Resolve(CultureInfo.GetCultureInfo(requested));
        Require(selection.RequestedLocale == requested);
        Require(selection.EffectiveUiLocale == effective);
        Require(selection.UsesEnglishFallback == fallback);
    }

    private static void Require(bool condition)
    {
        if (!condition)
            throw new InvalidOperationException("Phone localization assertion failed.");
    }
}
