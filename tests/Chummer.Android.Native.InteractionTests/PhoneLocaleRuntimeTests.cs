using System.Globalization;
using Chummer.Android.Native;
using Microsoft.Maui.Storage;

internal static class PhoneLocaleRuntimeTests
{
    public static void Run()
    {
        CultureInfo originalUi = CultureInfo.CurrentUICulture;
        CultureInfo originalFormats = CultureInfo.CurrentCulture;
        CultureInfo? originalDefaultUi = CultureInfo.DefaultThreadCurrentUICulture;
        CultureInfo? originalDefaultFormats = CultureInfo.DefaultThreadCurrentCulture;
        var preferences = new LocalePreferences();
        try
        {
            Require(PhoneLocalePolicy.ReadPreferences(preferences) == PhoneLocalePreferences.System,
                "New installs must follow the phone.");
            var choices = PhoneLocalePolicy.RegionChoices();
            Require(choices.Select(choice => choice.Value).Distinct().Count() == choices.Length
                && choices.Any(choice => choice.Value == "de-AT")
                && choices.Any(choice => choice.Value == "es-MX")
                && choices.Any(choice => choice.Value == "ja-JP"), "Regional choices are missing/duplicated.");

            foreach (var (language, region, expectedUi, expectedNumber) in new[]
            {
                // ICU versions legitimately differ in Austrian grouping separators.
                ("de", "de-AT", "de", 1234.56m.ToString("N2", CultureInfo.GetCultureInfo("de-AT"))),
                ("en", "de-DE", "en", "1.234,56"),
                ("es", "en-US", "es", "1,234.56")
            })
            {
                CultureInfo beforeUi = CultureInfo.CurrentUICulture;
                CultureInfo beforeFormats = CultureInfo.CurrentCulture;
                var selected = new PhoneLocalePreferences(language, region);
                PhoneLocalePolicy.SavePreferences(preferences, selected);
                Require(CultureInfo.CurrentUICulture == beforeUi && CultureInfo.CurrentCulture == beforeFormats,
                    "Saving must not change cultures midway through open forms.");
                // Reconstruct the preferences adapter as a new process would; no in-memory selection authority.
                preferences = new LocalePreferences(preferences.Values);
                Require(PhoneLocalePolicy.ReadPreferences(preferences) == selected, "Saved selection did not reopen.");
                PhoneLocalePolicy.InitializeFromPreferences(preferences,
                    CultureInfo.GetCultureInfo("en-GB"), CultureInfo.GetCultureInfo("en-GB"));
                Require(CultureInfo.CurrentUICulture.Name == expectedUi && CultureInfo.CurrentCulture.Name == region,
                    "Language and regional formatting must be independent.");
                Require(1234.56m.ToString("N2") == expectedNumber, $"Wrong decimal/group format for {region}: {1234.56m:N2}");
                Require(CultureInfo.DefaultThreadCurrentCulture?.Name == region
                    && CultureInfo.DefaultThreadCurrentUICulture?.Name == expectedUi,
                    "Background thread defaults were not initialized.");
                string label = PhoneStrings.Get("SettingsAppLanguage", "MISSING");
                Require(label == (language switch { "de" => "App-Sprache", "es" => "Idioma de la app", _ => "App language" }),
                    "Resource lookup did not use the persisted language.");
            }

            PhoneLocalePolicy.SavePreferences(preferences, PhoneLocalePreferences.System);
            PhoneLocaleSelection system = PhoneLocalePolicy.InitializeFromPreferences(preferences,
                CultureInfo.GetCultureInfo("es-MX"), CultureInfo.GetCultureInfo("de-AT"));
            Require(system.EffectiveUiLocale == "es-MX" && CultureInfo.CurrentCulture.Name == "de-AT",
                "Reset to phone settings retained an old override.");
            Require(PhoneLocalePolicy.SystemFormatCulture.Name == "de-AT", "System format preview uses wrong culture.");
            system = PhoneLocalePolicy.InitializeFromPreferences(preferences,
                CultureInfo.GetCultureInfo("ja-JP"), CultureInfo.GetCultureInfo("ja-JP"));
            Require(system.UsesEnglishFallback && system.EffectiveUiLocale == "en-US"
                && CultureInfo.CurrentCulture.Name == "ja-JP", "UI fallback must preserve regional formats.");

            foreach (string invalid in new[] { "junk", "1|fr|de-AT", "1|de|de", "1|en|not-a-region", "1|de|en-US|extra", new string('x', 129) })
            {
                preferences.Set(PhoneLocalePolicy.PreferencesKey, invalid);
                Require(PhoneLocalePolicy.ReadPreferences(preferences) == PhoneLocalePreferences.System,
                    "Invalid saved settings should fall back safely.");
            }
            string stored = preferences.Get(PhoneLocalePolicy.PreferencesKey, "");
            try
            {
                PhoneLocalePolicy.SavePreferences(preferences, new("fr", "en-US"));
                throw new InvalidOperationException("Unsupported selection was saved.");
            }
            catch (ArgumentException) { }
            Require(preferences.Get(PhoneLocalePolicy.PreferencesKey, "") == stored,
                "Rejected selection changed stored preferences.");
            Console.WriteLine("PASS phone language/region: EN/DE/ES, independent formats, save/reopen, system reset, corrupt-value fallback; no domain writes");
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUi;
            CultureInfo.CurrentCulture = originalFormats;
            CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUi;
            CultureInfo.DefaultThreadCurrentCulture = originalDefaultFormats;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class LocalePreferences : IPreferences
    {
        public Dictionary<string, object> Values { get; }
        public LocalePreferences(Dictionary<string, object>? values = null) => Values = values is null ? new() : new(values);
        public bool ContainsKey(string key, string? sharedName = null) => Values.ContainsKey(key);
        public void Remove(string key, string? sharedName = null) => Values.Remove(key);
        public void Clear(string? sharedName = null) => Values.Clear();
        public void Set<T>(string key, T value, string? sharedName = null) => Values[key] = value!;
        public T Get<T>(string key, T defaultValue, string? sharedName = null)
            => Values.TryGetValue(key, out object? value) ? (T)value : defaultValue;
    }
}
