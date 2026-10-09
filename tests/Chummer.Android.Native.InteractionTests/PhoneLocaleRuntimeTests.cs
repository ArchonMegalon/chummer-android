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
            SkillsCatalogCopy();
            MagicCatalogCopy();
            QualityCatalogCopy();
            RunnerFeedbackCopy();
            NewRunnerAndPrerequisiteCopy();
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

    private static void QualityCatalogCopy()
    {
        Guid ambidextrous = Guid.Parse("68cfe94a-fa7e-4129-a9b9-b5d73e3ced99");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-AT");
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        Require(QualityCatalogStrings.Name(ambidextrous, "Ambidextrous") == "Beidhändigkeit",
            "Quality names must follow UI language, not regional number formats.");
        Require(QualityCatalogStrings.MatchesSearch(ambidextrous, "Ambidextrous", null, "beidhändig")
            && QualityCatalogStrings.MatchesSearch(ambidextrous, "Ambidextrous", null, "ambidextrous")
            && QualityCatalogStrings.MatchesSearch(ambidextrous, "Ambidextrous", "Custom choice", "custom")
            && !QualityCatalogStrings.MatchesSearch(ambidextrous, "Ambidextrous", null, "no match"),
            "Quality search must retain German, original and follow-up labels.");
        Require(QualityCatalogStrings.Name(Guid.Empty, "Ambidextrous") == "Ambidextrous"
            && QualityCatalogStrings.Name(ambidextrous, "My custom quality") == "My custom quality"
            && QualityCatalogStrings.Name(ambidextrous, "ambidextrous") == "ambidextrous",
            "Unknown, renamed and case-changed quality names must remain unchanged.");
        foreach (string language in new[] { "en-US", "es-MX", "ja-JP" })
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            Require(QualityCatalogStrings.Name(ambidextrous, "Ambidextrous") == "Ambidextrous",
                "Missing quality translations must fall back to their original label.");
        }
    }

    private static void NewRunnerAndPrerequisiteCopy()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-AT");
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        DesktopDialogState dialog = new("dialog.new_character", "Select Build Method", null, [], []);
        var settings = new DesktopDialogField("newCharacterSetting", "Character Setting", "Core Rulebook", "Core Rulebook");
        var sr5 = dialog with { Fields = [new DesktopDialogField("newCharacterRulesetId", "Ruleset", "sr5", "")] };
        Require(NewRunnerDialogStrings.FixedSettingsDescription(dialog.Id, settings, sr5)!.Contains("alle Quellen")
            && NewRunnerDialogStrings.FixedSettingsDescription("custom", settings, sr5) is null
            && NewRunnerDialogStrings.FixedSettingsDescription(dialog.Id, settings with { Value = "custom" }, sr5) is null,
            "Fixed canonical profiles must be honest, localized and never overwrite custom values.");
        foreach (var (id, canonical, german) in new[] {
            ("newCharacterName", "Character Name", "Charaktername"),
            ("newCharacterAlias", "Alias", "Alias"),
            ("newCharacterRulesetId", "Ruleset", "Regelwerk"),
            ("newCharacterBuildMethod", "Build Method", "Erschaffungsmethode"),
            ("newCharacterSetting", "Character Setting", "Charakter-Einstellungsprofil"),
            ("newCharacterIgnoreRules", "Ignore Character Creation Rules", "Erschaffungsregeln ignorieren") })
        {
            DesktopDialogField field = new(id, canonical, "user-owned value", "canonical placeholder");
            NativeDialogScopedField original = new(true, field.Label, field.Options);
            NativeDialogScopedField display = NewRunnerDialogStrings.Project(dialog, field, original);
            Require(display.Label == german && display.IsVisible, $"New runner field untranslated: {id}");
            Require(field.Label == canonical && field.Value == "user-owned value"
                && field.Placeholder == "canonical placeholder", "Localization changed an editable field.");
            Require(NewRunnerDialogStrings.Project(dialog with { Id = "custom" }, field, original) == original,
                "Localization escaped the new runner dialog.");
            var custom = field with { Label = "custom caption" };
            Require(NewRunnerDialogStrings.Project(dialog, custom, original with { Label = custom.Label }).Label == custom.Label,
                "Unknown/custom field caption was overwritten.");
        }
        foreach (var (value, canonical, german) in new[] {
            ("Priority", "Priority", "Priorität"), ("SumToTen", "Sum-to-Ten", "Summe 10"),
            ("Karma", "Karma", "Karma"), ("LifeModule", "Life Modules", "Lebensmodule"),
            ("BP", "BP", "Generierungspunkte"),
            (Sr6CharacterCreationBuildMethods.Priority, "Priority", "Priorität"),
            (Sr6CharacterCreationBuildMethods.SumToTen, "Sum-to-Ten", "Summe 10"),
            (Sr6CharacterCreationBuildMethods.PointBuy, "Point Buy", "Punktekauf"),
            (Sr6CharacterCreationBuildMethods.LifePath, "Life Path", "Lebenspfad"),
            (Sr6CharacterCreationBuildMethods.Karma, "Karma (SR6)", "Karma (SR6)") })
        {
            DesktopDialogFieldOption option = new(value, canonical);
            DesktopDialogField field = new("newCharacterBuildMethod", "Build Method", value, value,
                InputType: "select", Options: [option]);
            NativeDialogScopedField display = NewRunnerDialogStrings.Project(dialog, field, new(true, field.Label, field.Options));
            Require(NewRunnerDialogStrings.MethodLabel(option) == german
                && display.Options is { Count: 1 } && display.Options[0].Label == german
                && display.Options[0].Value == value && field.Options is { Count: 1 } && field.Options[0] == option,
                "Translated method changed option count/order/identity or canonical state.");
            Require(NewRunnerDialogStrings.PickerTitle(dialog.Id, field, display.Label) == "Erschaffungsmethode auswählen",
                "Picker title leaked a canonical identifier.");
            Require(NewRunnerDialogStrings.MethodLabel(option with { Label = "custom method" }) == "custom method"
                && NewRunnerDialogStrings.MethodLabel(option with { Value = "unknown" }) == canonical,
                "Method identity/name mismatch should retain original label.");
        }
        Require(NativeDialogPage.ProjectNotice(dialog.Id, "Restored 11 runner dossiers.", null) == "11 Runner-Dossiers wiederhergestellt."
            && NativeDialogPage.ProjectNotice(dialog.Id, "Saved. Online sync failed.", null) == "Saved. Online sync failed."
            && NativeDialogPage.ProjectNotice("custom", "Saved.", "workspace") == "Saved.",
            "Dialog notice translation lost scope or concealed partial failure.");
        const string rankId = "55bc32fb-e097-4234-bdd5-a70e4f043b3d";
        Require(MagicCatalogStrings.OptionName("priority-rank", rankId, "A - Any metatype") == "A - Jeder Metatyp"
            && MagicCatalogStrings.MetatypeName("Human") == "Mensch"
            && MagicCatalogStrings.TalentName("Mundane") == "Mundan",
            "Prerequisite rank/heritage/talent names bypass German resources.");
        Require(MagicCatalogStrings.OptionName("priority-rank", rankId, "Custom heritage") == "Custom heritage"
            && MagicCatalogStrings.OptionName("priority-rank", "unknown", "A - Any metatype") == "A - Any metatype",
            "Changed or unknown rank authority acquired a misleading stock description.");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-GB");
        Require(NewRunnerDialogStrings.MethodLabel(new("LifeModule", "Life Modules")) == "Life Modules"
            && MagicCatalogStrings.OptionName("priority-rank", rankId, "A - Any metatype") == "A - Any metatype",
            "Neutral fallback changed the supplied rank or method.");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es-MX");
        Require(NewRunnerDialogStrings.MethodLabel(new("LifeModule", "Life Modules")) == "Módulos de vida",
            "New runner method lacks Spanish resources.");
        Console.WriteLine("PASS new runner/prerequisite copy: exact identity/name, DE/EN/ES, stable typed values, custom fallback, no domain writes");
    }

    private static void RunnerFeedbackCopy()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-AT");
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        foreach (var (canonical, german) in new[]
        {
            ("Restored 0 runner dossiers.", "0 Runner-Dossiers wiederhergestellt."),
            ("Restored 1 runner dossier.", "1 Runner-Dossier wiederhergestellt."),
            ("Restored 11 runner dossiers.", "11 Runner-Dossiers wiederhergestellt."),
            ("Saved.", "Gespeichert."),
            ("Application settings saved.", "Einstellungen gespeichert."),
            ("Account recovery is still finishing.", "Die Kontowiederherstellung wird noch abgeschlossen."),
            ("Workspace verification unavailable. Recent-file attribution was not recorded.",
                "Der Arbeitsstand konnte nicht überprüft werden. Die Zuordnung zur zuletzt geöffneten Datei wurde nicht gespeichert.")
        })
            Require(PhoneStrings.RunnerNotice(canonical) == german,
                $"Canonical runner feedback bypassed German display resources: {canonical}");

        // No substring replacement: preserve unknown failures, user text and
        // malformed counts verbatim. They must not become a success notice.
        foreach (string? unknown in new string?[] { null, "", " ", "Cannot save runner: disk full.",
            "Restored 1 runner dossiers.", "Restored 2 runner dossier.", "Restored 01 runner dossiers.",
            "Restored -2 runner dossiers.", "Restored +2 runner dossiers.", "Restored  2 runner dossiers.",
            "Restored 2147483648 runner dossiers.", "Restored 2 runner dossiers. Some files failed.",
            "Saved. Online sync failed.", "My runner is named Restored 11 runner dossiers." })
            Require(PhoneStrings.RunnerNotice(unknown) == unknown, "Unknown feedback was rewritten or lost.");

        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-GB");
        Require(PhoneStrings.RunnerNotice("Restored 11 runner dossiers.") == "Restored 11 runner dossiers.",
            "English feedback changed.");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es-MX");
        Require(PhoneStrings.RunnerNotice("Restored 1 runner dossier.") == "Se ha restaurado 1 expediente de runner."
            && PhoneStrings.RunnerNotice("Restored 11 runner dossiers.") == "Se han restaurado 11 expedientes de runner.",
            "Spanish feedback resources are missing.");
        Console.WriteLine("PASS runner feedback: DE/EN/ES, singular/plural/zero, unknown and partial failures retained; display only");
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

    private static void MagicCatalogCopy()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-AT");
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        var cases = new[] {
            (CharacterCreationMagicResonanceKinds.Spell, "c78d91cc-fa02-48c3-a243-28823a2038ef", "Acid Stream", "Säurestrahl"),
            (CharacterCreationMagicResonanceKinds.AdeptPower, "8caaadf4-75b4-4535-928a-5648d395c13a", "Adrenaline Boost", "Adrenalinschub"),
            (CharacterCreationMagicResonanceKinds.Tradition, "19320625-bc1a-492f-8904-da6a847e5700", "Hermetic", "Hermetisch"),
            (CharacterCreationMagicResonanceKinds.Stream, "7a3ecfbe-616e-425d-b204-329de37ffdbb", "Default", "Normal"),
            (CharacterCreationMagicResonanceKinds.ComplexForm, "373638b9-4334-4645-99f5-c3673e4f809b", "Cleaner", "Reiniger")
        };
        foreach (var (kind, id, name, german) in cases)
        {
            Require(MagicCatalogStrings.OptionName(kind, id, name) == german,
                "Magic catalog still displays English: " + kind);
            Require(MagicCatalogStrings.MatchesSearch(kind, id, name, "SR5", german.ToLowerInvariant())
                && MagicCatalogStrings.MatchesSearch(kind, id, name, "SR5", name)
                && MagicCatalogStrings.MatchesSearch(kind, id, name, "SR5", "sr5")
                && !MagicCatalogStrings.MatchesSearch(kind, id, name, "SR5", "no such entry"),
                "Search must accept displayed German, original names and source books.");
            Require(MagicCatalogStrings.OptionName(kind, id, "My custom choice") == "My custom choice"
                && MagicCatalogStrings.OptionName(kind, "new-source", name) == name
                && MagicCatalogStrings.OptionName("other-kind", id, name) == name,
                "Localization borrowed a label for a renamed/custom/different-kind identity.");
        }
        Require(MagicCatalogStrings.CategoryName(CharacterCreationMagicResonanceKinds.Spell, "Combat") == "Kampfzauber"
            && MagicCatalogStrings.CategoryName(CharacterCreationMagicResonanceKinds.Spell, "Custom category") == "Custom category"
            && MagicCatalogStrings.TalentName("Adept - 6 Magic") == "Adept - 6 Magie"
            && MagicCatalogStrings.MetatypeName("Human") == "Mensch"
            && MagicCatalogStrings.TalentName("My custom talent") == "My custom talent",
            "Talent, metatype or category labels were not preserved/localized correctly.");
        foreach (string language in new[] { "en-GB", "es-MX", "ja-JP" })
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            foreach (var (kind, id, name, _) in cases)
                Require(MagicCatalogStrings.OptionName(kind, id, name) == name,
                    "Non-German catalog fallback changed.");
        }
        Console.WriteLine("PASS Magic catalog: five canonical kinds, DE/AT independent of US region, localized/original/book search, custom identity isolation and EN/ES fallback");
    }

    private static void SkillsCatalogCopy()
    {
        const string mechanic = "b52f7575-eebf-41c4-938d-df3397b5ee68";
        const string arabic = "05eb2251-81aa-4dfb-8619-75102fc7b895";
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-AT");
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        Require(SkillCatalogStrings.SkillName("active", mechanic, "Aeronautics Mechanic") == "Luftfahrtmechanik",
            "Canonical active skill still displays English.");
        Require(SkillCatalogStrings.SkillName("knowledge", arabic, "Arabic") == "Arabisch"
            && SkillCatalogStrings.GroupName("Engineering") == "Mechanik"
            && SkillCatalogStrings.CategoryName("Technical Active") == "Technische Aktionsfertigkeiten",
            "Language, group or category ignored German UI culture.");
        Require(SkillCatalogStrings.SpecializationName("active", mechanic, "Aeronautics Mechanic", "Fixed Wing") == "Starrflügler",
            "Admitted specialization ignored German catalog.");
        Require(SkillCatalogStrings.SkillName("active", mechanic, "My renamed skill") == "My renamed skill"
            && SkillCatalogStrings.SkillName("active", "custom-id", "Aeronautics Mechanic") == "Aeronautics Mechanic"
            && SkillCatalogStrings.SkillName("knowledge", mechanic, "Aeronautics Mechanic") == "Aeronautics Mechanic"
            && SkillCatalogStrings.SpecializationName("active", mechanic, "My renamed skill", "Fixed Wing") == "Fixed Wing"
            && SkillCatalogStrings.SpecializationName("active", mechanic, "Aeronautics Mechanic", "My custom spec") == "My custom spec",
            "Custom, renamed, unknown or different-kind catalog data was rewritten.");
        Require(SkillCatalogStrings.GroupName("My custom group") == "My custom group"
            && SkillCatalogStrings.CategoryName("My custom category") == "My custom category", "Custom label changed.");
        foreach (string language in new[] { "en-US", "es-MX", "fr-FR" })
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            Require(SkillCatalogStrings.SkillName("active", mechanic, "Aeronautics Mechanic") == "Aeronautics Mechanic"
                && SkillCatalogStrings.SpecializationName("active", mechanic, "Aeronautics Mechanic", "Fixed Wing") == "Fixed Wing",
                "Unavailable translation must preserve canonical English, not leak German or an ID.");
        }
        Console.WriteLine("PASS skill catalog labels: DE/US, active/knowledge/native language/group/category/spec, exact ID/name/kind matching, custom fallback, EN/ES fallback");
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
