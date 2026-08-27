using System.Globalization;
using Chummer.Android.Native;

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

AndroidSurfaceStrings.AssertExactKeyParity();

AndroidSurfaceCopy german = AndroidSurfaceStrings.Resolve("de-AT");
AndroidSurfaceCopy english = AndroidSurfaceStrings.Resolve("en-GB");
AndroidSurfaceCopy spanish = AndroidSurfaceStrings.Resolve("es-MX");
AndroidSurfaceCopy unsupported = AndroidSurfaceStrings.Resolve("fr-FR");
AndroidSurfaceCopy invalid = AndroidSurfaceStrings.Resolve("not-a-culture!");

Assert(german.ResourceLanguage == "de" && german.DisplayCulture.Name == "de-AT" && !german.UsesEnglishFallback,
    "de-AT must select the complete German pack while preserving its display culture.");
Assert(english.ResourceLanguage == "en" && english.DisplayCulture.Name == "en-GB" && !english.UsesEnglishFallback,
    "en-GB must select the complete English pack while preserving its display culture.");
Assert(spanish.ResourceLanguage == "es" && spanish.DisplayCulture.Name == "es-MX" && !spanish.UsesEnglishFallback,
    "es-MX must select the complete Spanish pack while preserving its display culture.");
Assert(unsupported.ResourceLanguage == "en" && unsupported.DisplayCulture.Name == "en-US" && unsupported.UsesEnglishFallback,
    "Unsupported UI languages must visibly select the complete en-US fallback pack.");
Assert(invalid.ResourceLanguage == "en" && invalid.DisplayCulture.Name == "en-US" && invalid.UsesEnglishFallback,
    "Invalid culture names must fail closed to the complete en-US fallback pack.");

foreach (string key in AndroidSurfaceStrings.Keys)
{
    Assert(!string.IsNullOrWhiteSpace(german[key]), $"Missing German value for {key}.");
    Assert(!string.IsNullOrWhiteSpace(english[key]), $"Missing English value for {key}.");
    Assert(!string.IsNullOrWhiteSpace(spanish[key]), $"Missing Spanish value for {key}.");
}

Assert(german["Stories.Public"] == "Öffentliche Runner-Geschichten", "German Stories copy was not selected.");
Assert(spanish["Gear.Title"] == "Equipo", "Spanish Gear copy was not selected.");
Assert(german.Format("Resources.OptionDetail", 2, 4_000, 54_000).Contains("2 Karma", StringComparison.Ordinal),
    "de-AT placeholder formatting failed.");
Assert(spanish.Format("Stories.Chapter", 2, 10).Contains("2", StringComparison.Ordinal),
    "es-MX placeholder formatting failed.");

Console.WriteLine($"PASS Android surface localization: {AndroidSurfaceStrings.Keys.Count} keys × 3 catalogs; 5 locale cases.");
