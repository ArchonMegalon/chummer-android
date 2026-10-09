using System.Globalization;
using Chummer.Android.Native;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;
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
            GermanCreationCopy();
            RegionalCreationCopy();
            SkillsBlockerCopy();
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

    private static void GermanCreationCopy()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-AT");
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-AT");
        Require(BuildPageUiProjection.SaveToolbarText(false) == "Speichern"
            && BuildPageUiProjection.SaveToolbarText(true) == "Gespeichert.", "Toolbar leaked English.");
        Require(BuildPageUiProjection.RouteMarker(null).Label == "Kein Runner geöffnet", "Empty route leaked English.");
        Require(BuildPageUiProjection.StageLabel(CharacterCreationWizardStepIds.Skills, "Skills") == "Fertigkeiten"
            && BuildPageUiProjection.MethodLabel(CharacterCreationBuildMethods.Priority) == "Priorität"
            && BuildPageUiProjection.MethodLabel(CharacterCreationBuildMethods.LifeModules) == "Lebensmodule",
            "Canonical creation labels bypassed German resources.");

        // Check every canonical stage, method and budget, not only one attractive screenshot.
        foreach (var (type, prefix) in new[] {
            (typeof(CharacterCreationWizardStepIds), "CreationStep."),
            (typeof(CharacterCreationBuildMethods), "CreationMethod."),
            (typeof(CharacterCreationWizardStepStatuses), "CreationStatus."),
            (typeof(CharacterCreationBudgetIds), "CreationBudget.") })
        foreach (var field in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            if (!field.IsLiteral || field.FieldType != typeof(string)) continue;
            string id = (string)field.GetRawConstantValue()!;
            Require(PhoneStrings.Get(prefix + id, "MISSING") != "MISSING", $"Untranslated canonical label: {prefix}{id}");
        }
        var budget = new CharacterCreationBudgetState(CharacterCreationBudgetIds.NormalAttributes,
            "Normal attributes", 20, 17.5m, 2.5m, true, [], "points");
        var original = budget with { };
        Require(BuildPageUiProjection.BudgetLabel(budget) == "Normale Attribute"
            && BuildPageUiProjection.BudgetUnit(budget.Unit) == "Punkte"
            && BuildPageUiProjection.BudgetRemaining(budget.Remaining) == "2,5 übrig", "Budget copy or regional number failed.");
        Require(budget == original && budget.Label == "Normal attributes" && budget.Unit == "points",
            "Display localization mutated the authority.");
        Require(CreationAttributesPage.FormatBudget(2.5m, "points") == "2,5 Punkte"
            && CreationAttributesPage.FormatBudget(25, "karma") == "25 Karma"
            && CreationAttributesPage.FormatBudget(2.5m, "custom") == "2,5 custom"
            && CreationAttributesPage.FormatBudget(2.5m, "") == "2,5",
            "Attribute editor units bypassed German resources or changed unknown units.");
        // Ready Attributes/Skills replace snapshot budgets with editor-owned
        // ledgers. Their IDs need not equal the snapshot's display identity.
        foreach (var (canonicalId, expectedLabel) in new[] {
            (CharacterCreationBudgetIds.NormalAttributes, "Normale Attribute"),
            (CharacterCreationBudgetIds.SpecialAttributes, "Spezialattribute"),
            (CharacterCreationBudgetIds.ActiveSkills, "Aktive Fertigkeiten"),
            (CharacterCreationBudgetIds.SkillGroups, "Fertigkeitsgruppen"),
            (CharacterCreationBudgetIds.KnowledgeSkills, "Wissensfertigkeiten") })
        {
            var typedLedger = budget with { BudgetId = "editor-owned-ledger", Label = "English editor points" };
            Require(BuildPageUiProjection.BudgetLabel(typedLedger, canonicalId) == expectedLabel,
                $"Typed ledger bypassed canonical German display identity: {canonicalId}");
            Require(typedLedger.Remaining == 2.5m && typedLedger.BudgetId == "editor-owned-ledger",
                "Localization changed typed ledger values or identity.");
        }
        Require(BuildPageUiProjection.BudgetLabel(budget with { BudgetId = CharacterCreationMagicResonancePresentationBudgetIds.AdeptPowerPoints }) == "Kraftpunkte",
            "Adept budget leaked English.");
        Require(BuildPageUiProjection.BudgetLabel(budget with { BudgetId = "friends-in-high-places-contacts", Label = "Friends in High Places contacts" }) == "Hochrangige Connections",
            "Additional contact budget leaked English.");
        Require(BuildPageUiProjection.StageLabel("future-custom-stage", "My Custom Stage") == "My Custom Stage",
            "Unknown/custom labels must not be guessed or rewritten.");
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        Require(BuildPageUiProjection.BudgetRemaining(2.5m) == "2.5 übrig", "App language overrode the chosen region.");
        Require(CreationAttributesPage.FormatBudget(2.5m, "points") == "2.5 Punkte",
            "Attribute editor ignored the separately selected region.");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-GB");
        Require(BuildPageUiProjection.StageLabel("skills", "MISSING") == "Skills", "English fallback changed.");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es-MX");
        Require(BuildPageUiProjection.StageLabel("skills", "MISSING") == "Habilidades", "Spanish fallback changed.");
        Console.WriteLine("PASS localized creation dashboard: all canonical labels, toolbar, DE/AT and DE/US numbers, immutable authority, custom fallback, EN/ES");
    }

    private static void RegionalCreationCopy()
    {
        foreach (var (language, region) in new[] { ("de-AT", "en-US"), ("en-GB", "de-AT"), ("es-MX", "de-DE") })
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(region);
            string template = CreationAllocationStrings.Get("Skills.BudgetLeft", "MISSING");
            Require(template != "MISSING", "Actual Skills satellite resource missing.");
            Require(CreationAllocationStrings.Format("Skills.BudgetLeft", "MISSING", 2.5m)
                == string.Format(CultureInfo.CurrentCulture, template, 2.5m),
                "Creation formatting ignored the independently selected region.");
            var explicitCulture = CultureInfo.GetCultureInfo("de-DE");
            Require(CreationAllocationStrings.Format(explicitCulture, "Skills.BudgetLeft", "MISSING", 2.5m)
                == string.Format(explicitCulture, CreationAllocationStrings.Get("Skills.BudgetLeft", "MISSING", explicitCulture), 2.5m),
                "An explicit locale override changed behavior.");
        }
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-AT");
        Require(CreationAllocationStrings.AttributeName("AGI") == "Geschicklichkeit"
            && CreationAllocationStrings.AttributeName("LOG") == "Logik"
            && CreationAllocationStrings.AttributeName("custom-attribute") == "custom-attribute",
            "Skill-linked attribute labels were untranslated or unknown labels were rewritten.");
        Console.WriteLine("PASS Creation regional formatting: independent DE/US, EN/AT, ES/DE, explicit override and canonical skill attributes");
    }

    private static void SkillsBlockerCopy()
    {
        string[] codes = typeof(CharacterCreationSkillsBlockers)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Concat([CharacterCreationSkillsReReviewSchemas.Unavailable, CharacterCreationSkillsReReviewSchemas.Stale,
                CharacterCreationSkillsReReviewSchemas.ExplicitReviewRequired]).ToArray();
        foreach (string language in new[] { "de-AT", "en-GB", "es-MX" })
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            string unknown = CreationAllocationStrings.SkillBlocker("future-code-1a2b3c4d");
            Require(unknown.Length > 20 && !unknown.Contains("future-code", StringComparison.Ordinal),
                "Unknown diagnostic code leaked into player copy.");
            foreach (string code in codes)
            {
                string message = CreationAllocationStrings.SkillBlocker(code);
                Require(message != unknown && !message.Contains(code, StringComparison.Ordinal),
                    $"Known Skills blocker lacks useful localized copy: {language}/{code}");
            }
            Require(CreationAllocationStrings.SkillBlocker(CharacterCreationSkillsBlockers.PostCommitRefreshRequired)
                != CreationAllocationStrings.SkillBlocker(CharacterCreationSkillsBlockers.IdempotencyConflict),
                "Saved-but-refresh-failed was confused with an uncertain/rejected save.");
        }
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-AT");
        Require(CreationAllocationStrings.SkillBlocker(CharacterCreationSkillsBlockers.ActiveBudgetExceeded)
            .Contains("Fertigkeitspunkte", StringComparison.Ordinal), "German budget guidance is still English.");
        Console.WriteLine("PASS all canonical Skills/re-review reasons: EN/DE/ES player guidance, unknown-code fallback, distinct save outcomes");
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
