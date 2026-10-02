using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Presentation.Overview;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr5;
using Microsoft.Maui.Storage;
using System.Text.Json;

/// <summary>Actual source catalogs/services and file persistence, with the ordinary
/// phone draft and Presentation projection. This is not an Android handler or device proof.</summary>
internal static class CreationMagicNativeRuntimeTests
{
    internal static bool HasBloodSpellSummary(string book, string name) =>
        book == "FA" && new[] { "Boil Blood", "Corpse Explosion", "Embolism", "Giger Spit",
            "Ice Veins", "Pyrohemetics", "Rupture", "Clot", "Share Damage", "Somatic Healing",
            "Sympathetic Reprisal", "Blood Puppet", "Corpse Spikes", "Corpse Lash",
            "Blood Whip", "Blood Blade", "Viscera Web" }.Contains(name);

    internal static bool HasOrdinaryArcanaSummary(string book, string name) =>
        book is "BB" or "BTB"
        || book == "FA" && new[] { "Branch", "Vines", "Thorn", "Rosebush", "Growth",
            "Lash", "Slash", "Claw", "Barrage", "Multiply Food", "Comet", "Gravity",
            "Gravity Well", "Evil Eye", "Alter Ballistics" }.Contains(name);

    internal static bool HasNewSupplementSummary(string book, string category, string name) =>
        book is "SS" or "CA"
        || book == "HT" && (category is "Combat" or "Manipulation")
        || book == "SG" && new[] { "Bind", "Net Bind", "Mana Bind", "Mana Net", "Bug Zapper",
            "Calm Animal", "Calm Pack", "Catfall", "Clean [Element]", "Compel Truth",
            "Control Animal", "Control Pack", "Deflection" }.Contains(name);

    public static void RunSkillsReReview(string contentRoot) => RunTalent(contentRoot, technomancer: false, aspectedGroup: "Sorcery");
    public static void RunCheckpointRecovery(string contentRoot) => RunTalent(contentRoot, technomancer: false);
    public static void RunMagicReReview(string contentRoot) => RunTalent(contentRoot, technomancer: false, magicReReview: true);
    public static void RunMysticReadability(string contentRoot)
    {
        VerifySpellDescriptions(contentRoot);
        RunTalent(contentRoot, technomancer: false, mysticAdept: true);
    }

    private static void VerifySpellDescriptions(string contentRoot)
    {
        var resolver = new FileSystemCharacterSourceDataResolver(
            new FileSystemContentOverlayCatalogService(contentRoot, contentRoot, null));
        var context = resolver.TryCreateContext("<character><settings>"
            + CharacterCreationBootstrapProfiles.PrioritySettingsProfileId + "</settings></character>");
        Require(context is not null && context.TryResolveCreationMagicResonanceAuthority(out _), "Spell help needs actual Core content.");
        context!.TryResolveCreationMagicResonanceAuthority(out var authority);
        string before = JsonSerializer.Serialize(authority);
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            foreach (string locale in new[] { "en-GB", "de-AT", "es-MX" })
            {
                System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(locale);
                int authored = 0;
                foreach (var spell in authority.Spells)
                {
                    string summary = CreationSpellInfo.Summary(spell);
                    Require(!summary.Contains(CreationFlowStrings.Get("Spells.Unavailable", "missing"))
                        && !summary.Contains(spell.Identity.SourceId) && summary.Length > 15,
                        "Actual spell has no readable source-bound profile: " + spell.Name + "/" + locale);
                    string prose = CreationFlowStrings.Get("Spells.Summary." + spell.Identity.SourceId, string.Empty);
                    if (prose.Length == 0) continue;
                    authored++;
                    Require(summary.StartsWith(prose, StringComparison.Ordinal), "Reviewed spell definition hash mismatched: " + spell.Name);
                    var xml = System.Xml.Linq.XElement.Parse(spell.CanonicalSourceXml);
                    xml.Element("dv")!.Value = "F+99";
                    string changed = xml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
                    var custom = spell with { CanonicalSourceXml = changed,
                        CanonicalSourceXmlDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8(changed) };
                    string amended = CreationSpellInfo.Summary(custom);
                    Require(!amended.Contains(prose) && amended.Contains("F+99")
                        && amended.Contains(CreationFlowStrings.Get("Spells.Changed", "missing")),
                        "Changed custom rules retained the original spell explanation.");
                    Require(CreationSpellInfo.Summary(custom with { CanonicalSourceXmlDigest = spell.CanonicalSourceXmlDigest })
                        == CreationFlowStrings.Get("Spells.Unavailable", "missing"), "Tampered spell payload displayed trusted help.");
                }
                Require(authored == 292, $"Expected 292 reviewed spell summaries; got {authored} in {locale}.");
                var coreCombat = authority.Spells.Where(spell => spell.SourceBook == "SR5" && spell.Category == "Combat").ToArray();
                Require(coreCombat.Length == 18, "Expected the complete SR5 core combat catalog.");
                foreach (var spell in coreCombat)
                {
                    string prose = CreationFlowStrings.Get("Spells.Summary." + spell.Identity.SourceId, string.Empty);
                    Require(prose.Length > 0 && CreationSpellInfo.Summary(spell).StartsWith(prose, StringComparison.Ordinal),
                        "A core combat spell still has only a numeric profile: " + spell.Name + "/" + locale);
                }
                var coreDetection = authority.Spells.Where(spell => spell.SourceBook == "SR5" && spell.Category == "Detection").ToArray();
                Require(coreDetection.Length == 18, "Expected the complete SR5 core detection catalog.");
                foreach (var spell in coreDetection)
                {
                    string prose = CreationFlowStrings.Get("Spells.Summary." + spell.Identity.SourceId, string.Empty);
                    string summary = CreationSpellInfo.Summary(spell);
                    Require(prose.Length > 0 && summary.StartsWith(prose, StringComparison.Ordinal)
                        && summary.Contains(CreationFlowStrings.Get("Spells.Detection.Touch", "missing"))
                        && !summary.Contains(CreationFlowStrings.Get("Spells.Range.T", "missing")),
                        "Detection help must explain the effect and distinguish casting from sensing: " + spell.Name + "/" + locale);
                }
                foreach (string name in new[] { "Detect Enemies", "Detect Life", "Detect [Life Form]", "Detect Magic" })
                {
                    string normal = CreationSpellInfo.Summary(coreDetection.Single(spell => spell.Name == name));
                    string extended = CreationSpellInfo.Summary(coreDetection.Single(spell => spell.Name == name + ", Extended"));
                    string area = CreationFlowStrings.Get("Spells.Sensing.Area", "missing");
                    string extendedArea = CreationFlowStrings.Get("Spells.Sensing.Extended Area", "missing");
                    Require(normal.Contains(area) && !normal.Contains(extendedArea)
                        && extended.Contains(extendedArea) && !extended.Contains(area),
                        "Normal and extended senses need distinct source-derived profiles: " + name + "/" + locale);
                }
                Require(CreationSpellInfo.Summary(coreDetection.Single(spell => spell.Name == "Mind Probe"))
                        .Contains(CreationFlowStrings.Get("Spells.Sensing.Directional", "missing"))
                    && CreationSpellInfo.Summary(coreDetection.Single(spell => spell.Name == "Mindlink"))
                        .Contains(CreationFlowStrings.Get("Spells.Sensing.Psychic", "missing")),
                    "Directional and psychic spells must retain their actual sensing descriptors.");
                var coreHealth = authority.Spells.Where(spell => spell.SourceBook == "SR5" && spell.Category == "Health").ToArray();
                Require(coreHealth.Length == 11, "Expected the complete SR5 core health catalog.");
                foreach (var spell in coreHealth)
                {
                    string prose = CreationFlowStrings.Get("Spells.Summary." + spell.Identity.SourceId, string.Empty);
                    string summary = CreationSpellInfo.Summary(spell);
                    Require(prose.Length > 0 && summary.StartsWith(prose, StringComparison.Ordinal)
                        && summary.Contains(CreationFlowStrings.Get("Spells.Range.T", "missing"))
                        && !summary.Contains(CreationFlowStrings.Get("Spells.Detection.Touch", "missing")),
                        "A core health spell lacks its own source-bound, ordinary touch-range explanation: " + spell.Name + "/" + locale);
                }
                var coreIllusion = authority.Spells.Where(spell => spell.SourceBook == "SR5" && spell.Category == "Illusion").ToArray();
                Require(coreIllusion.Length == 19, "Expected the complete SR5 core illusion catalog.");
                foreach (var spell in coreIllusion)
                {
                    string prose = CreationFlowStrings.Get("Spells.Summary." + spell.Identity.SourceId, string.Empty);
                    string summary = CreationSpellInfo.Summary(spell);
                    Require(prose.Length > 0 && summary.StartsWith(prose, StringComparison.Ordinal)
                        && summary.Contains(CreationFlowStrings.Get("Spells.Duration.S", "missing"))
                        && !summary.Contains("Damage: 0."),
                        "A core illusion lacks its source-bound effect/sustained profile: " + spell.Name + "/" + locale);
                }
                var coreManipulation = authority.Spells.Where(spell => spell.SourceBook == "SR5" && spell.Category == "Manipulation").ToArray();
                Require(coreManipulation.Length == 18, "Expected the complete SR5 core manipulation catalog.");
                foreach (var spell in coreManipulation)
                {
                    string prose = CreationFlowStrings.Get("Spells.Summary." + spell.Identity.SourceId, string.Empty);
                    Require(prose.Length > 0 && CreationSpellInfo.Summary(spell).StartsWith(prose, StringComparison.Ordinal),
                        "A core manipulation spell still has only its profile: " + spell.Name + "/" + locale);
                }
                Require(coreCombat.Length + coreDetection.Length + coreHealth.Length
                    + coreIllusion.Length + coreManipulation.Length == 84,
                    "The five ordinary SR5 core spell categories must retain all 84 definitions; rituals are separate.");
                var grimoireHealth = authority.Spells.Where(spell => spell.SourceBook == "SG" && spell.Category == "Health").ToArray();
                Require(grimoireHealth.Length == 11, "Expected the complete Street Grimoire health catalog.");
                foreach (var spell in grimoireHealth)
                {
                    string prose = CreationFlowStrings.Get("Spells.Summary." + spell.Identity.SourceId, string.Empty);
                    string summary = CreationSpellInfo.Summary(spell);
                    Require(prose.Length > 0 && summary.StartsWith(prose, StringComparison.Ordinal)
                        && summary.Contains(CreationFlowStrings.Get("Spells.Range.T", "missing"))
                        && !summary.Contains("Damage: 0."),
                        "A Street Grimoire health spell lacks its source-bound touch effect: " + spell.Name + "/" + locale);
                }
                var grimoireCombat = authority.Spells.Where(spell => spell.SourceBook == "SG" && spell.Category == "Combat").ToArray();
                Require(grimoireCombat.Length == 22, "Expected the complete Street Grimoire combat catalog.");
                foreach (var spell in grimoireCombat)
                {
                    string prose = CreationFlowStrings.Get("Spells.Summary." + spell.Identity.SourceId, string.Empty);
                    Require(prose.Length > 0 && CreationSpellInfo.Summary(spell).StartsWith(prose, StringComparison.Ordinal),
                        "A Street Grimoire combat spell still has only its profile: " + spell.Name + "/" + locale);
                }
                var grimoireDetection = authority.Spells.Where(spell => spell.SourceBook == "SG" && spell.Category == "Detection").ToArray();
                Require(grimoireDetection.Length == 21, "Expected the complete Street Grimoire detection catalog.");
                foreach (var spell in grimoireDetection)
                {
                    string prose = CreationFlowStrings.Get("Spells.Summary." + spell.Identity.SourceId, string.Empty);
                    Require(prose.Length > 0 && CreationSpellInfo.Summary(spell).StartsWith(prose, StringComparison.Ordinal),
                        "A Street Grimoire detection spell still has only its profile: " + spell.Name + "/" + locale);
                }
                var grimoireIllusion = authority.Spells.Where(spell => spell.SourceBook == "SG" && spell.Category == "Illusion").ToArray();
                Require(grimoireIllusion.Length == 17, "Expected the complete Street Grimoire illusion catalog.");
                foreach (var spell in grimoireIllusion)
                {
                    string prose = CreationFlowStrings.Get("Spells.Summary." + spell.Identity.SourceId, string.Empty);
                    string summary = CreationSpellInfo.Summary(spell);
                    Require(prose.Length > 0 && summary.StartsWith(prose, StringComparison.Ordinal)
                        && !summary.Contains("Damage: 0."),
                        "A Street Grimoire illusion lacks its source-bound effect: " + spell.Name + "/" + locale);
                    string expectedDuration = spell.Name == "Switch Vehicle Signature" ? "Special" : "S";
                    Require(summary.Contains(CreationFlowStrings.Get("Spells.Duration." + expectedDuration, "missing")),
                        "Illusion help lost its accepted duration: " + spell.Name + "/" + locale);
                }
                var grimoireManipulation = authority.Spells.Where(spell => spell.SourceBook == "SG" && spell.Category == "Manipulation").ToArray();
                Require(grimoireManipulation.Length == 42, "Expected the complete Street Grimoire manipulation catalog.");
                foreach (var spell in grimoireManipulation)
                {
                    string prose = CreationFlowStrings.Get("Spells.Summary." + spell.Identity.SourceId, string.Empty);
                    string summary = CreationSpellInfo.Summary(spell);
                    var xml = System.Xml.Linq.XElement.Parse(spell.CanonicalSourceXml);
                    Require(prose.Length > 0 && summary.StartsWith(prose, StringComparison.Ordinal)
                        && !summary.Contains("Damage: 0."),
                        "A Street Grimoire manipulation lacks its own source-bound effect: " + spell.Name + "/" + locale);
                    foreach (var (field, element) in new[] { ("Range", "range"), ("Duration", "duration"), ("Type", "type") })
                        Require(summary.Contains(CreationFlowStrings.Get("Spells." + field + "." + xml.Element(element)!.Value, "missing")),
                            "Manipulation help lost its accepted profile: " + spell.Name + "/" + field + "/" + locale);
                    Require(summary.Contains(CreationFlowStrings.Format("Spells.Drain", "missing", xml.Element("dv")!.Value)),
                        "Manipulation help replaced the accepted Drain: " + spell.Name + "/" + locale);
                }
                Require(grimoireCombat.Length + grimoireDetection.Length + grimoireHealth.Length
                    + grimoireIllusion.Length + grimoireManipulation.Length == 113,
                    "All 113 ordinary Street Grimoire spells need effect summaries; rituals are separate.");
                var shadowSpells = authority.Spells.Where(spell => spell.SourceBook == "SSP" && spell.Category != "Rituals").ToArray();
                Require(shadowSpells.Length == 41
                    && shadowSpells.Count(spell => spell.Category == "Combat") == 6
                    && shadowSpells.Count(spell => spell.Category == "Detection") == 6
                    && shadowSpells.Count(spell => spell.Category == "Health") == 12
                    && shadowSpells.Count(spell => spell.Category == "Illusion") == 3
                    && shadowSpells.Count(spell => spell.Category == "Manipulation") == 14,
                    "Expected all 41 ordinary Shadow Spells definitions; rituals remain separate.");
                foreach (var spell in shadowSpells)
                {
                    string prose = CreationFlowStrings.Get("Spells.Summary." + spell.Identity.SourceId, string.Empty);
                    string summary = CreationSpellInfo.Summary(spell);
                    var xml = System.Xml.Linq.XElement.Parse(spell.CanonicalSourceXml);
                    Require(prose.Length > 0 && summary.StartsWith(prose, StringComparison.Ordinal)
                        && !summary.Contains("Damage: 0."),
                        "Shadow Spells help lacks its own exact-source effect: " + spell.Name + "/" + locale);
                    foreach (var (field, element) in new[] { ("Range", "range"), ("Duration", "duration"), ("Type", "type") })
                    {
                        string key = field == "Range" && spell.Category == "Detection" && xml.Element(element)!.Value == "T"
                            ? "Spells.Detection.Touch" : "Spells." + field + "." + xml.Element(element)!.Value;
                        string expected = CreationFlowStrings.Get(key, "missing");
                        Require(expected != "missing" && summary.Contains(expected),
                            "Shadow Spells help lost its localized Core profile: " + spell.Name + "/" + field + "/" + locale);
                    }
                    Require(summary.Contains(CreationFlowStrings.Format("Spells.Drain", "missing", xml.Element("dv")!.Value)),
                        "Shadow Spells help replaced accepted Drain: " + spell.Name + "/" + locale);
                }
                var ordinaryArcana = authority.Spells.Where(spell =>
                    HasOrdinaryArcanaSummary(spell.SourceBook, spell.Name)).ToArray();
                Require(ordinaryArcana.Length == 19
                    && ordinaryArcana.Count(spell => spell.SourceBook == "FA") == 15
                    && ordinaryArcana.Count(spell => spell.SourceBook == "BB") == 2
                    && ordinaryArcana.Count(spell => spell.SourceBook == "BTB") == 2,
                    "Expected all 19 selected ordinary spells; blood spells and rituals remain separate.");
                var bloodSpells = authority.Spells.Where(spell =>
                    HasBloodSpellSummary(spell.SourceBook, spell.Name)).ToArray();
                Require(bloodSpells.Length == 17 && bloodSpells.All(spell =>
                    System.Xml.Linq.XElement.Parse(spell.CanonicalSourceXml).Element("required") is not null),
                    "Expected all 17 blood spells with their original prerequisites.");
                foreach (var spell in ordinaryArcana.Concat(bloodSpells))
                {
                    string prose = CreationFlowStrings.Get("Spells.Summary." + spell.Identity.SourceId, string.Empty);
                    string summary = CreationSpellInfo.Summary(spell);
                    var xml = System.Xml.Linq.XElement.Parse(spell.CanonicalSourceXml);
                    Require(prose.Length > 0 && summary.StartsWith(prose, StringComparison.Ordinal)
                        && !summary.Contains("Damage: 0."),
                        "A supplement spell lacks its exact-source effect: " + spell.Name + "/" + locale);
                    foreach (var (field, element) in new[] { ("Range", "range"), ("Duration", "duration"), ("Type", "type") })
                    {
                        string key = field == "Range" && spell.Category == "Detection" && xml.Element(element)!.Value == "T"
                            ? "Spells.Detection.Touch" : "Spells." + field + "." + xml.Element(element)!.Value;
                        string expected = CreationFlowStrings.Get(key, "missing");
                        Require(expected != "missing" && summary.Contains(expected),
                            "Supplement help lost its Core profile: " + spell.Name + "/" + field + "/" + locale);
                    }
                    Require(summary.Contains(CreationFlowStrings.Format("Spells.Drain", "missing", xml.Element("dv")!.Value)),
                        "Supplement help replaced accepted Drain: " + spell.Name + "/" + locale);
                }
                var supplementSpells = authority.Spells.Where(spell =>
                    HasNewSupplementSummary(spell.SourceBook, spell.Category, spell.Name)).ToArray();
                Require(supplementSpells.Length == 31
                    && supplementSpells.Count(spell => spell.SourceBook == "SS") == 8
                    && supplementSpells.Count(spell => spell.SourceBook == "CA") == 4
                    && supplementSpells.Count(spell => spell.SourceBook == "HT") == 6
                    && supplementSpells.Count(spell => spell.SourceBook == "SG") == 13,
                    "Expected the complete selected supplement catalogs, without including rituals.");
                foreach (var spell in supplementSpells)
                {
                    string prose = CreationFlowStrings.Get("Spells.Summary." + spell.Identity.SourceId, string.Empty);
                    string summary = CreationSpellInfo.Summary(spell);
                    var xml = System.Xml.Linq.XElement.Parse(spell.CanonicalSourceXml);
                    Require(prose.Length > 0 && summary.StartsWith(prose, StringComparison.Ordinal)
                        && !summary.Contains("Damage: 0."),
                        "A supplement spell lacks its own effect: " + spell.Name + "/" + spell.SourceBook + "/" + locale);
                    foreach (var (field, element) in new[] { ("Range", "range"), ("Duration", "duration"), ("Type", "type") })
                    {
                        string expected = CreationFlowStrings.Get("Spells." + field + "." + xml.Element(element)!.Value, "missing");
                        Require(expected != "missing" && summary.Contains(expected),
                            "Supplement help lost its localized accepted profile: " + spell.Name + "/" + field + "/" + locale);
                    }
                    Require(summary.Contains(CreationFlowStrings.Format("Spells.Drain", "missing", xml.Element("dv")!.Value)),
                        "Supplement help replaced accepted Drain: " + spell.Name + "/" + locale);
                }
                foreach (var (normalName, extendedName) in new[] {
                    ("Mindnet", "Mindnet Extended"), ("Spatial Sense", "Spatial Sense, Extended") })
                {
                    string normal = CreationSpellInfo.Summary(grimoireDetection.Single(spell => spell.Name == normalName));
                    string extended = CreationSpellInfo.Summary(grimoireDetection.Single(spell => spell.Name == extendedName));
                    string normalSense = CreationFlowStrings.Get("Spells.Sensing.Area", "missing");
                    string extendedSense = CreationFlowStrings.Get("Spells.Sensing.Extended Area", "missing");
                    Require(normal.Contains(normalSense) && !normal.Contains(extendedSense)
                        && extended.Contains(extendedSense) && !extended.Contains(normalSense)
                        && new[] { normal, extended }.All(summary =>
                            summary.Contains(CreationFlowStrings.Get("Spells.Range.T (A)", "missing"))),
                        "Grimoire area casting and normal/extended sensing profiles were conflated: " + normalName + "/" + locale);
                }
                Console.WriteLine($"PASS spell help: {authority.Spells.Count} exact profiles, {authored} authored effects ({locale}), custom-data/tamper rejection");
            }
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-GB");
            Require(!CreationSpellInfo.Summary(authority.Spells.Single(row => row.Name == "Levitate"))
                .Contains("Damage: 0."), "Non-damaging spell help exposes a zero-value placeholder.");
            string fireball = CreationSpellInfo.Summary(authority.Spells.Single(row => row.Name == "Fireball"));
            string heal = CreationSpellInfo.Summary(authority.Spells.Single(row => row.Name == "Heal"));
            Require(heal.StartsWith("Repairs Physical injuries, not Stun damage.", StringComparison.Ordinal)
                && heal.Contains("F-4") && !heal.Contains("Damage: 0."),
                "Heal must explain the effect without an invented Stun healing or zero-damage rule.");
            string reflexes = CreationSpellInfo.Summary(authority.Spells.Single(row => row.Name == "Increase Reflexes"));
            Require(reflexes.StartsWith("Improves Initiative and adds Initiative dice.", StringComparison.Ordinal)
                && reflexes.Contains("Drain: F (F = Force).") && !reflexes.Contains("F-1"),
                "Effect prose must not replace this definition's Drain with quick-start values.");
            string antidote = CreationSpellInfo.Summary(authority.Spells.Single(row => row.Name == "Antidote"));
            string detox = CreationSpellInfo.Summary(authority.Spells.Single(row => row.Name == "Detox"));
            string disease = CreationSpellInfo.Summary(authority.Spells.Single(row => row.Name == "Cure Disease"));
            string pain = CreationSpellInfo.Summary(authority.Spells.Single(row => row.Name == "Resist Pain"));
            string stabilize = CreationSpellInfo.Summary(authority.Spells.Single(row => row.Name == "Stabilize"));
            Require(antidote.StartsWith("Adds hits to the upcoming poison-resistance test.", StringComparison.Ordinal)
                && antidote.Contains("Drain: F-3")
                && detox.StartsWith("Removes drug/poison symptoms, not damage.", StringComparison.Ordinal)
                && detox.Contains("Drain: F-6"),
                "Poison resistance and symptom relief must not be confused or inherit each other's profile.");
            Require(disease.StartsWith("Boosts disease resistance, without healing existing damage.", StringComparison.Ordinal)
                && pain.StartsWith("Reduces wound penalties without healing.", StringComparison.Ordinal)
                && stabilize.StartsWith("Stops further overflow deterioration, without repairing injuries.", StringComparison.Ordinal)
                && new[] { disease, pain, stabilize }.All(summary => summary.Contains("Drain: F-4")),
                "Resistance, wound modifiers and overflow stabilization must not promise wound repair.");
            string prophylaxis = CreationSpellInfo.Summary(authority.Spells.Single(row => row.Name == "Prophylaxis"));
            Require(prophylaxis.StartsWith("Boosts disease/toxin resistance; also weakens beneficial drugs.", StringComparison.Ordinal)
                && prophylaxis.Contains("Must be sustained.") && prophylaxis.Contains("Drain: F-4"),
                "Preventive resistance must retain the medicine tradeoff and actual sustained profile.");
            string increase = CreationSpellInfo.Summary(authority.Spells.Single(row => row.Name == "Increase [Attribute]"));
            string decrease = CreationSpellInfo.Summary(authority.Spells.Single(row => row.Name == "Decrease [Attribute]"));
            Require(increase.StartsWith("Raises one Physical or Mental attribute.", StringComparison.Ordinal)
                && decrease.StartsWith("Reduces one Physical or Mental attribute.", StringComparison.Ordinal)
                && increase.Contains("Drain: F-3") && decrease.Contains("Drain: F-2"),
                "Attribute spell help must retain opposite effects and distinct costs.");
            string SpellHelp(string name, string? book = null) => CreationSpellInfo.Summary(
                authority.Spells.Single(spell => spell.Name == name && (book is null || spell.SourceBook == book)));
            foreach (string name in new[] { "Vines", "Comet", "Mass Astral Disruption" })
                Require(SpellHelp(name).Contains("Target within line of sight.")
                    && !SpellHelp(name).Contains("Area within line of sight."),
                    "A help-only edit must not silently substitute another source's range: " + name);
            Require(SpellHelp("Alter Ballistics").Contains("Requires touch.")
                && SpellHelp("Alter Ballistics").Contains("Instant effect.")
                && SpellHelp("Death Replay").Contains("Touch casting; separate sensing range.")
                && SpellHelp("Incision").Contains("Requires touch.")
                && SpellHelp("Incision").Contains("Must be sustained."),
                "Effect help must keep alchemy, sensing and ordinary touch profiles separate.");
            foreach (var (single, area) in new[] { ("Bind", "Net Bind"), ("Mana Bind", "Mana Net"),
                ("Calm Animal", "Calm Pack"), ("Control Animal", "Control Pack"), ("Incubus", "Incubus Shroud") })
                Require(SpellHelp(single).Contains("Target within line of sight.")
                    && SpellHelp(area).Contains("Area within line of sight."),
                    "Supplement single/area variants lost their source targeting: " + single);
            Require(SpellHelp("Bind").Contains("Physical spell.") && SpellHelp("Mana Bind").Contains("Mana spell.")
                && SpellHelp("Calm Animal").Contains("self-defense remains")
                && SpellHelp("Control Animal").Contains("Commands non-sapient")
                && SpellHelp("Compel Truth").Contains("not silence")
                && SpellHelp("Compel Truth").Contains("Special duration.")
                && SpellHelp("Catfall").Contains("Reduces falling damage"),
                "Binding, calming, commanding and protection must retain distinct effects and limits.");
            foreach (string name in new[] { "Sound Barrier", "Vehicle Mask" })
                Require(SpellHelp(name, "SS").Contains("Drain: F-2")
                    && !SpellHelp(name, "SS").Contains("Drain: F-3")
                    && SpellHelp(name, "SG").Contains("Drain: F-3")
                    && !SpellHelp(name, "SG").Contains("Drain: F-2"),
                    "Same-name source variants were conflated: " + name);
            Require(SpellHelp("Interference", "SS").Contains("radio and wireless")
                && SpellHelp("Interference", "SS").Contains("Drain: F-2")
                && SpellHelp("Interference", "SG").Contains("Drain: F-1")
                && SpellHelp("Interference", "SG").Contains("radio and wireless")
                && SpellHelp("Fashion", "SS").Contains("without improving protection")
                && SpellHelp("Fashion", "SG").Contains("without improving protection"),
                "Reviewed same-name effects must preserve their separate accepted source profiles.");
            Require(SpellHelp("Glue", "SG").Contains("one target")
                && SpellHelp("Glue Strip", "SG").Contains("across an area")
                && SpellHelp("Glue", "SG").Contains("Target within line of sight.")
                && SpellHelp("Glue Strip", "SG").Contains("Area within line of sight.")
                && SpellHelp("Fix", "SG").Contains("missing parts cannot be recreated")
                && SpellHelp("Reinforce", "SG").Contains("Armor and Structure"),
                "Binding, repair and reinforcement must preserve their different effects and limits.");
            Require(SpellHelp("Increase Noise", "SG").Contains("Worsens local Matrix reception")
                && SpellHelp("Decrease Noise", "SG").Contains("Improves local Matrix reception")
                && SpellHelp("Increase Gear Limits", "SG").Contains("Raises one limit")
                && SpellHelp("Decrease Gear Limits", "SG").Contains("Lowers one limit"),
                "Noise is Matrix interference, not audio; gear spells change one equipment limit, not every attribute.");
            Require(new[] { "Shapechange", "[Critter] Form" }.All(name =>
                    SpellHelp(name, "SG").Contains("willing subject")
                    && SpellHelp(name, "SG").Contains("mind and equipment stay unchanged"))
                && SpellHelp("Shape [Material]", "SG").Contains("without creating more")
                && SpellHelp("Turn To Goo", "SG").Contains("equipment and implants remain intact"),
                "Body and material changes must not promise equipment transformation or material creation.");
            Require(SpellHelp("Spirit Barrier", "SG").Contains("not other magic")
                && SpellHelp("Spirit Zapper", "SG").StartsWith("Barrier against spirits and their powers", StringComparison.Ordinal)
                && SpellHelp("Offensive Mana Barrier", "SG").Contains("injures spirits, dual beings and astral forms")
                && SpellHelp("Pulse", "SG").Contains("RFID tags")
                && SpellHelp("Pulse", "SSP").Contains("standard RFID tags")
                && SpellHelp("Pulse", "SG").Contains("Drain: F+3")
                && SpellHelp("Pulse", "SSP").Contains("Drain: F-4"),
                "Defensive/offensive barriers and same-name Pulse definitions must not borrow unsupported effects.");
            Require(SpellHelp("Passenger", "SSP").Contains("all of the target's senses")
                && SpellHelp("Passenger", "SSP").Contains("Must be sustained.")
                && SpellHelp("Passenger", "SSP").Contains("Drain: F (F = Force).")
                && SpellHelp("Inflict Disease", "SSP").Contains("never a magical infection")
                && SpellHelp("Inflict Disease", "SSP").Contains("Mana spell.")
                && SpellHelp("Inflict Disease", "SSP").Contains("Becomes permanent after completion.")
                && SpellHelp("Inflict Disease", "SSP").Contains("Drain: F-3")
                && SpellHelp("Secret Handshake", "SSP").Contains("Physical spell."),
                "Independently authored effects must not import another printing's numeric/type/duration profile.");
            Require(SpellHelp("Sunbeam", "SSP").Contains("sunlight-allergic")
                && SpellHelp("Sunbeam", "SSP").Contains("weaker Stun")
                && SpellHelp("Flame Burst", "SSP").Contains("can burn allies")
                && SpellHelp("Flame Burst", "SSP").Contains("Area centered on the caster.")
                && SpellHelp("Chill", "SSP").Contains("lowers Initiative")
                && SpellHelp("Frigid", "SSP").Contains("Area within line of sight."),
                "Combat summaries must retain conditional damage, friendly-fire and exact area limits.");
            Require(new[] { "Ghoulish Strength", "Vampiric Speed", "Vampiric Stealth" }
                    .All(name => SpellHelp(name, "SSP").Contains("only") && SpellHelp(name, "SSP").Contains("HMHVV-infected"))
                && SpellHelp("Vampiric Stealth", "SSP").Contains("Affects the caster.")
                && SpellHelp("Vampiric Speed", "SSP").Contains("Area centered on the caster.")
                && SpellHelp("Decontamination", "SSP").Contains("without repairing existing injuries")
                && SpellHelp("Alleviate Nausea", "SSP").Contains("symptoms can return")
                && SpellHelp("Personal Warmth", "SSP").Contains("without protection against cold attacks")
                && SpellHelp("Rot", "SSP").Contains("Heal cannot repair"),
                "Infected-only bonuses and symptom relief must not promise universal benefits or injury healing.");
            Require(SpellHelp("Broadcast", "SSP").Contains("one-way")
                && SpellHelp("Broadcast", "SSP").Contains("not just allies")
                && SpellHelp("Sending", "SSP").Contains("Sensing: extended area.")
                && SpellHelp("False Impression", "SSP").Contains("cannot create a new aura")
                && SpellHelp("Manascape", "SSP").Contains("cannot create new auras")
                && SpellHelp("Recorded Room", "SSP").Contains("covered text stays hidden"),
                "Perception and illusion help must preserve their scope instead of inventing unrestricted sensing.");
            Require(SpellHelp("Air Filter", "SSP").Contains("underwater or while buried")
                && SpellHelp("Air Filter", "SSP").Contains("Mana spell.")
                && SpellHelp("Evaporate", "SSP").Contains("cooling from sweat")
                && SpellHelp("Astral Armor", "SSP").Contains("not physical attacks")
                && SpellHelp("Insulate", "SSP").Contains("fixed area")
                && SpellHelp("Insulate", "SSP").Contains("moderate temperature differences")
                && SpellHelp("Petrify", "SSP").Contains("can still be injured")
                && SpellHelp("Alter Memory", "SSP").Contains("original can eventually return"),
                "Environmental, astral, bodily and memory effects must retain their practical limitations.");
            Require(SpellHelp("Hibernate").Contains("does not maintain unconsciousness")
                && SpellHelp("Nutrition").Contains("risks addiction")
                && SpellHelp("Intoxication").Contains("drunkenness and fatigue")
                && SpellHelp("Rewind").Contains("recent memories permanently")
                && SpellHelp("Consistency").Contains("telepathic message or image")
                && SpellHelp("Consistency").Contains("Instant effect.")
                && !SpellHelp("Consistency").Contains("Touch casting"),
                "Supplement help must distinguish its real effect from a similar name or symptom.");
            Require(SpellHelp("Catch").Contains("unattended object")
                && SpellHelp("Snakeblood").Contains("thermal detection")
                && SpellHelp("Conceal Scent").Contains("object's scent")
                && SpellHelp("Recharge Potency").Contains("preparation once")
                && SpellHelp("Recharge Potency").Contains("Must be sustained.")
                && !SpellHelp("Recharge Potency").Contains("Becomes permanent"),
                "Hard Targets effects must not rewrite the accepted Core profile from another source.");
            Require(SpellHelp("Manablade").Contains("bypasses armor, not for physical parries")
                && SpellHelp("Powerblade").Contains("can parry, armor resists")
                && new[] { "Manablade", "Powerblade" }.All(name => SpellHelp(name).Contains("Caster-only")
                    && SpellHelp(name).Contains("Special range.") && SpellHelp(name).Contains("Must be sustained.")),
                "The two caster-only weapon spells must retain their different defenses and special range.");
            Require(SpellHelp("Astral Message").Contains("messages")
                && SpellHelp("Astral Clairvoyance").Contains("distant auras")
                && new[] { "Astral Clairvoyance", "Mana Window", "Astral Window", "[Sense] Cryptesthesia" }
                    .All(name => SpellHelp(name).Contains("no spell targeting"))
                && SpellHelp("Mana Window").StartsWith("Physical remote sight", StringComparison.Ordinal)
                && SpellHelp("Astral Window").StartsWith("Remote assensing", StringComparison.Ordinal),
                "Remote physical/astral senses must not promise line-of-sight spell targeting.");
            Require(SpellHelp("Borrow Sense").Contains("one sense, not control")
                && SpellHelp("Animal Sense").Contains("mundane, non-sapient")
                && SpellHelp("Eyes of the Pack").Contains("sight from willing")
                && SpellHelp("Night Vision").Contains("low-light")
                && SpellHelp("Hawkeye").Contains("visual Perception")
                && SpellHelp("Enhance Aim").Contains("distance penalties"),
                "Borrowed senses, enhanced vision and aiming must keep their distinct effects and limits.");
            Require(SpellHelp("Catalog").Contains("nonliving items")
                && SpellHelp("Diagnose").Contains("without healing")
                && SpellHelp("Diagnose").Contains("Instant effect.")
                && !SpellHelp("Diagnose").Contains("Must be sustained.")
                && SpellHelp("Dragon Astral Signature").Contains("lingering magic")
                && SpellHelp("Dragon Astral Signature").Contains("Drain: F+5"),
                "Inventory, diagnosis and dragon detection must retain their distinct accepted profiles.");
            Require(new[] { "Mindnet", "Mindnet Extended" }.All(name => SpellHelp(name).Contains("willing minds"))
                && SpellHelp("Mindnet").Contains("Drain: F (F = Force).")
                && SpellHelp("Mindnet Extended").Contains("Drain: F+1")
                && new[] { "Spatial Sense", "Spatial Sense, Extended" }
                    .All(name => SpellHelp(name).Contains("not creatures/security"))
                && SpellHelp("Spatial Sense").Contains("Drain: F-3")
                && SpellHelp("Spatial Sense, Extended").Contains("Drain: F-1"),
                "Group telepathy and spatial layout must preserve scope limits and exact variant costs.");
            Require(new[] { "Thought Recognition", "Area Thought Recognition" }
                    .All(name => SpellHelp(name).Contains("chosen surface thought"))
                && SpellHelp("Thought Recognition").Contains("Touch casting; separate sensing range.")
                && SpellHelp("Area Thought Recognition").Contains("Area within line of sight.")
                && !SpellHelp("Area Thought Recognition").Contains("Touch casting")
                && SpellHelp("Translate").Contains("one speaker's intent, not exact wording"),
                "Thought recognition must not imply unrestricted mind probing, and translation must not promise exact wording.");
            foreach (var (single, area) in new[] { ("Decoy", "Chaff"), ("Euphoria", "Opium Den"),
                ("[Sense] Removal", "Mass [Sense] Removal"), ("Stink", "Stench") })
                Require(SpellHelp(single, "SG").Contains("Target within line of sight.")
                    && SpellHelp(area, "SG").Contains("Area within line of sight."),
                    "Illusion variants lost their actual single/area casting profiles: " + single);
            Require(SpellHelp("Camouflage", "SG").Contains("Mana spell.")
                && SpellHelp("Camouflage", "SG").Contains("Drain: F-2")
                && SpellHelp("Physical Camouflage", "SG").Contains("Physical spell.")
                && SpellHelp("Physical Camouflage", "SG").Contains("Drain: F (F = Force).")
                && SpellHelp("Double Image", "SG").Contains("Requires touch.")
                && SpellHelp("Double Image", "SG").Contains("Drain: F-1")
                && SpellHelp("Switch Vehicle Signature", "SG").Contains("Drain: F+1")
                && SpellHelp("Vehicle Mask", "SG").Contains("Drain: F-3"),
                "Illusion summaries must retain the actual Core profiles rather than values from another printing.");
            foreach (var (touch, single, area) in new[] {
                ("Corrode [Object]", "Melt [Object]", "Sludge [Object]"),
                ("Ram [Object]", "Wreck [Object]", "Demolish [Object]"),
                ("One Less [Metatype/Species]", "Slay [Metatype/Species]", "Slaughter [Metatype/Species]") })
                Require(SpellHelp(touch).Contains("Requires touch.")
                    && SpellHelp(single).Contains("Target within line of sight.")
                    && SpellHelp(area).Contains("Area within line of sight."),
                    "Specialized combat variants lost their exact targeting profiles: " + touch);
            foreach (var (single, area) in new[] { ("Firewater", "Napalm"), ("Ice Spear", "Ice Storm"),
                ("Radiation Beam", "Radiation Burst"), ("Pollutant Stream", "Pollutant Wave") })
                Require(SpellHelp(single).Contains("Target within line of sight.")
                    && SpellHelp(area).Contains("Area within line of sight.")
                    && SpellHelp(single).Contains("Indirect magical attack.")
                    && SpellHelp(area).Contains("Indirect magical attack."),
                    "Elemental help changed the accepted single/area or indirect profile: " + single);
            Require(SpellHelp("Corrode [Object]").Contains("Corrodes")
                && SpellHelp("Corrode [Object]").Contains("Drain: F-5")
                && SpellHelp("Melt [Object]").Contains("Drain: F-3")
                && new[] { "Ram [Object]", "Wreck [Object]", "Demolish [Object]" }
                    .All(name => SpellHelp(name).Contains("excludes vehicles"))
                && SpellHelp("Destroy [Vehicle]").Contains("vehicle class")
                && SpellHelp("Destroy [Vehicle]").Contains("Physical spell."),
                "Object-specific effects must not replace the accepted object/vehicle profiles with another printing.");
            Require(SpellHelp("Disrupt [Focus]").Contains("Temporarily")
                && SpellHelp("Disrupt [Focus]").Contains("active focus")
                && SpellHelp("Destroy [Free Spirit]").Contains("one designated")
                && SpellHelp("Insecticide [Insect Spirit]").Contains("insect-spirit type")
                && SpellHelp("Insecticide [Insect Spirit]").Contains("Mana spell.")
                && SpellHelp("Insecticide [Insect Spirit]").Contains("Area within line of sight."),
                "Focus disruption and spirit-specific attacks lost their restrictions or accepted profiles.");
            Require(new[] { "Firewater", "Napalm" }.All(name => SpellHelp(name).Contains("fire and water"))
                && SpellHelp("Napalm").Contains("Drain: F (F = Force).")
                && SpellHelp("Ice Spear").Contains("cold damage")
                && !SpellHelp("Ice Spear").Contains("slippery")
                && SpellHelp("Ice Storm").Contains("slippery")
                && SpellHelp("Ice Storm").Contains("Drain: F+1")
                && SpellHelp("Slaughter [Metatype/Species]").Contains("Drain: F-2")
                && SpellHelp("Shattershield").Contains("mana barriers")
                && SpellHelp("Shattershield").Contains("Requires touch."),
                "Combat explanations lost distinct elemental/barrier effects or replaced current Core values.");
            Require(SpellHelp("Awaken").Contains("temporarily") && SpellHelp("Awaken").Contains("afterward")
                && SpellHelp("Awaken").Contains("Must be sustained.")
                && SpellHelp("Alleviate Addiction").Contains("not Focus")
                && SpellHelp("Alleviate Addiction").Contains("no cure")
                && SpellHelp("Alleviate [Allergy]").Contains("without curing")
                && SpellHelp("Alleviate [Allergy]").Contains("Physical spell."),
                "Temporary relief must retain its limits and the accepted catalog type.");
            Require(SpellHelp("Crank").Contains("risks addiction")
                && SpellHelp("Fast").Contains("without nourishing")
                && SpellHelp("Enabler").Contains("Weakens resistance")
                && SpellHelp("Ambidexterity").Contains("Temporarily")
                && new[] { "Crank", "Fast", "Enabler", "Ambidexterity" }
                    .All(name => SpellHelp(name).Contains("Must be sustained.") && SpellHelp(name).Contains("Drain: F-3")),
                "Grimoire health help must preserve side effects and accepted sustained profiles.");
            Require(SpellHelp("Decrease Reflexes").Contains("not Initiative dice")
                && SpellHelp("Decrease Reflexes").Contains("Becomes permanent after completion.")
                && SpellHelp("Forced Defense").Contains("retreat remains possible")
                && SpellHelp("Forced Defense").Contains("Instant effect."),
                "Grimoire timing profiles and defensive choice limits must remain distinct.");
            Require(SpellHelp("Increase Inherent Limits").StartsWith("Raises one", StringComparison.Ordinal)
                && SpellHelp("Decrease Inherent Limits").StartsWith("Lowers one", StringComparison.Ordinal)
                && new[] { "Increase Inherent Limits", "Decrease Inherent Limits" }
                    .All(name => SpellHelp(name).Contains("Physical/Mental/Social") && SpellHelp(name).Contains("Drain: F-1")),
                "Opposite limit effects must not change every limit or inherit another spell profile.");
            foreach (var (single, area) in new[] { ("Agony", "Mass Agony"), ("Bugs", "Swarm"),
                ("Confusion", "Mass Confusion"), ("Chaos", "Chaotic World") })
                Require(SpellHelp(single).Contains("Target within line of sight.")
                    && !SpellHelp(single).Contains("Area within line of sight.")
                    && SpellHelp(area).Contains("Area within line of sight."),
                    "Individual and area illusion variants lost distinct target profiles: " + single);
            Require(SpellHelp("Bugs").Contains("Initiative") && SpellHelp("Swarm").Contains("Initiative")
                && SpellHelp("Mass Confusion").Contains("dice pools")
                && SpellHelp("Chaos").Contains("technological sensors")
                && SpellHelp("Chaotic World").Contains("technological sensors")
                && SpellHelp("Chaotic World").Contains("Drain: F (F = Force)."),
                "Illusion effects or the accepted Core Drain were replaced by another spell/printing.");
            Require(SpellHelp("Entertainment").Contains("Obvious illusions; invisible to sensors.")
                && SpellHelp("Trid Entertainment").Contains("Obvious illusions, also perceived by sensors.")
                && SpellHelp("Phantasm").Contains("Convincing illusions for living observers.")
                && SpellHelp("Trid Phantasm").Contains("Convincing illusions, also affecting sensors."),
                "Obvious/realistic and living/sensor illusion variants must remain distinct.");
            Require(SpellHelp("Hush").Contains("living listeners") && SpellHelp("Hush").Contains("Mana spell.")
                && SpellHelp("Silence").Contains("microphones") && SpellHelp("Silence").Contains("Physical spell.")
                && SpellHelp("Stealth").Contains("subject's own noises")
                && SpellHelp("Stealth").Contains("Target within line of sight.")
                && !SpellHelp("Stealth").Contains("Area within line of sight."),
                "Sound masking must distinguish listeners, sensors and the subject from an area.");
            var phantasm = authority.Spells.Single(spell => spell.Name == "Phantasm");
            var physicalXml = System.Xml.Linq.XElement.Parse(phantasm.CanonicalSourceXml);
            physicalXml.Element("type")!.Value = "P";
            string physicalSource = physicalXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
            string customPhysical = CreationSpellInfo.Summary(phantasm with { CanonicalSourceXml = physicalSource,
                CanonicalSourceXmlDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8(physicalSource) });
            Require(customPhysical.Contains("Definition changed;") && customPhysical.Contains("Physical spell.")
                && !customPhysical.Contains("living observers") && !customPhysical.Contains("Mana spell."),
                "Changing an illusion's type retained stale authored sensor semantics.");
            foreach (var (single, area) in new[] { ("Animate", "Mass Animate"),
                ("Control Actions", "Mob Control"), ("Control Thoughts", "Mob Mind") })
                Require(SpellHelp(single).Contains("Target within line of sight.")
                    && SpellHelp(single).Contains("Drain: F-1")
                    && SpellHelp(area).Contains("Area within line of sight.")
                    && SpellHelp(area).Contains("Drain: F+1"),
                    "Manipulation variants lost the accepted source range or Drain: " + single);
            Require(SpellHelp("Animate").Contains("without fine control")
                && SpellHelp("Mass Animate").Contains("without fine control")
                && SpellHelp("Control Actions").Contains("body, not thoughts")
                && SpellHelp("Mob Control").Contains("body, not thoughts")
                && SpellHelp("Control Thoughts").Contains("commands seem self-chosen")
                && SpellHelp("Mob Mind").Contains("commands seem self-chosen"),
                "Object movement, body control and mental commands must remain distinct.");
            Require(SpellHelp("Mana Barrier").Contains("not ordinary matter")
                && SpellHelp("Mana Barrier").Contains("Mana spell.")
                && SpellHelp("Physical Barrier").Contains("gases pass")
                && SpellHelp("Physical Barrier").Contains("Physical spell."),
                "Mana and physical barriers must not be described as the same obstruction.");
            Require(SpellHelp("Fling").Contains("Instant effect.")
                && !SpellHelp("Fling").Contains("Damage: 0.")
                && SpellHelp("Poltergeist").Contains("battering nearby targets")
                && !SpellHelp("Poltergeist").Contains("Damage: 0.")
                && SpellHelp("Ignite").Contains("after completion")
                && SpellHelp("Ignite").Contains("Becomes permanent after completion.")
                && !SpellHelp("Ignite").Contains("Instant effect."),
                "Zero catalog placeholders or timing must not misrepresent damaging manipulation effects.");
            Require(fireball.Contains("Indirect magical attack") && fireball.Contains("Physical damage")
                && fireball.Contains("Area within line of sight") && fireball.Contains("F-1"), "Area spell profile lost its concrete properties.");
            string acid = CreationSpellInfo.Summary(authority.Spells.Single(row => row.Name == "Acid Stream"));
            string lightning = CreationSpellInfo.Summary(authority.Spells.Single(row => row.Name == "Lightning Bolt"));
            string knockout = CreationSpellInfo.Summary(authority.Spells.Single(row => row.Name == "Knockout"));
            string stunball = CreationSpellInfo.Summary(authority.Spells.Single(row => row.Name == "Stunball"));
            Require(acid.StartsWith("Burns with corrosive acid.", StringComparison.Ordinal)
                && lightning.StartsWith("Strikes with magical lightning.", StringComparison.Ordinal)
                && fireball.StartsWith("Burns with magical flames.", StringComparison.Ordinal),
                "Elemental spell explanations lost their distinguishing effects.");
            Require(knockout.StartsWith("Directly stuns the target.", StringComparison.Ordinal)
                && stunball.StartsWith("Directly stuns the target.", StringComparison.Ordinal)
                && knockout.Contains("Requires touch.") && stunball.Contains("Area within line of sight")
                && !knockout.Contains("Physical damage") && !stunball.Contains("Physical damage"),
                "Shared effect prose must preserve each spell's actual range and damage type.");
            var detection = authority.Spells.Single(spell => spell.Name == "Detect Enemies, Extended");
            foreach (string descriptor in new[] { "Active, Area", "Active, Directional", "Active, Unknown", string.Empty })
            {
                var xml = System.Xml.Linq.XElement.Parse(detection.CanonicalSourceXml);
                xml.Element("descriptor")!.Value = descriptor;
                string changed = xml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
                string summary = CreationSpellInfo.Summary(detection with { CanonicalSourceXml = changed,
                    CanonicalSourceXmlDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8(changed) });
                Require(summary.Contains("Definition changed;") && !summary.Contains("Senses hostile intentions.")
                    && !summary.Contains("Sensing: extended area.")
                    && (summary.Contains("Sensing: surrounding area.") == (descriptor == "Active, Area"))
                    && (summary.Contains("Sensing: directional.") == (descriptor == "Active, Directional")),
                    "Custom detection sensing must follow the accepted descriptor, never the name or stale help.");
                if (descriptor is "Active, Unknown" or "")
                    Require(!summary.Contains("Sensing:"), "Unknown/missing detection descriptors invented a sense.");
            }
            Require(heal.Contains("Requires touch.") && !heal.Contains("separate sensing range"),
                "Detection wording leaked into an ordinary touch-range health spell.");
            Require(JsonSerializer.Serialize(authority) == before, "Reading spell help mutated the rules catalog.");
        }
        finally { System.Globalization.CultureInfo.CurrentUICulture = previous; }
    }

    public static void RunSumToTen(string contentRoot)
    {
        RunTalent(contentRoot, technomancer: false, buildMethod: CharacterCreationBuildMethods.SumToTen);
        RunTalent(contentRoot, technomancer: true, buildMethod: CharacterCreationBuildMethods.SumToTen);
        RunTalent(contentRoot, technomancer: false, mysticAdept: true, buildMethod: CharacterCreationBuildMethods.SumToTen);
        RunTalent(contentRoot, technomancer: false, aspectedGroup: "Sorcery", buildMethod: CharacterCreationBuildMethods.SumToTen);
    }

    // Test-only fixture export for a bounded real Android editor/save/restart smoke.
    // Core performs bootstrap and prerequisite mutations; no hand-written authority.
    public static void ExportSumToTenMagicSeed(string contentRoot, string directory)
        => ExportMagicSeed(contentRoot, directory, mysticAdept: false);

    public static void ExportMysticMagicSeed(string contentRoot, string directory)
        => ExportMagicSeed(contentRoot, directory, mysticAdept: true);

    private static void ExportMagicSeed(string contentRoot, string directory, bool mysticAdept)
    {
        Require(Path.IsPathFullyQualified(directory) && Directory.Exists(directory)
            && !Directory.EnumerateFileSystemEntries(directory).Any(), "Seed destination must be explicit and empty.");
        RunTalent(contentRoot, technomancer: false, buildMethod: CharacterCreationBuildMethods.SumToTen,
            seedDirectory: directory, mysticAdept: mysticAdept);
    }

    public static void Run(string contentRoot)
    {
        foreach ((string locale, string title) in new[]
                 { ("de-AT", "Talentauswahl"), ("en-GB", "Talent choices"), ("es-MX", "Opciones del talento") })
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo(locale);
            Require(CreationFlowStrings.Get("TalentChoices.Title", "missing", culture) == title,
                "Talent choice title did not use the actual satellite resource.");
            foreach (string key in new[] { "SelectionOnly", "Continue", "ChooseMore", "SelectionSlot" })
                Require(CreationFlowStrings.Get("TalentChoices." + key, "missing", culture) != "missing", key);
            Require(CreationFlowStrings.Format(culture, "TalentChoices.SelectionSlot", "missing", 1, "Sorcery")
                    .Contains("Sorcery", StringComparison.Ordinal),
                "Translated copy must preserve the Core-provided selected group.");
        }
        foreach (string aspect in new[] { "Conjuring", "Enchanting", "Sorcery" })
            RunTalent(contentRoot, technomancer: false, aspectedGroup: aspect);
        RunTalent(contentRoot, technomancer: false);
        RunTalent(contentRoot, technomancer: true);
        RunTalent(contentRoot, technomancer: false, mysticAdept: true);
    }

    private static void RunTalent(string contentRoot, bool technomancer, bool mysticAdept = false,
        string? aspectedGroup = null, string buildMethod = CharacterCreationBuildMethods.Priority,
        string? seedDirectory = null, bool magicReReview = false)
    {
        Require(Path.IsPathFullyQualified(contentRoot) && Directory.Exists(Path.Combine(contentRoot, "data")),
            "Supply the explicit Core content directory.");
        string directory = Directory.CreateTempSubdirectory("chummer-native-magic-").FullName;
        try
        {
            var store = new FileWorkspaceStore(directory);
            var resolver = new FileSystemCharacterSourceDataResolver(
                new FileSystemContentOverlayCatalogService(contentRoot, contentRoot, null));
            var queries = new XmlCharacterFileQueries(new CharacterFileService());
            var codec = new Sr5WorkspaceCodec(queries,
                new XmlCharacterSectionQueries(new CharacterSectionService(resolver)),
                new XmlCharacterMetadataCommands(new CharacterFileService()));
            var bootstrap = new CharacterCreationBootstrapService(store,
                new RulesetWorkspaceCodecResolver([codec]), queries, resolver);
            var created = bootstrap.Create(new(CharacterCreationBootstrapSchemas.RequestV1,
                CharacterCreationBootstrapStages.AwaitingFoundationSelection, RulesetDefaults.Sr5,
                "Awakened phone projection", technomancer ? "Technomancer" : "Adept", buildMethod,
                buildMethod == CharacterCreationBuildMethods.SumToTen
                    ? CharacterCreationBootstrapProfiles.SumToTenSettingsProfileId
                    : CharacterCreationBootstrapProfiles.PrioritySettingsProfileId));
            Require(created.Outcome == CharacterCreationBootstrapOutcomes.Success, string.Join(",", created.Blockers));
            var id = created.Value!.WorkspaceId;
            var prerequisites = new CharacterCreationPrerequisiteService(store, queries, resolver);
            var initial = prerequisites.Load(new(id)).Value!;
            var ranks = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [CharacterCreationPriorityCategoryIds.Heritage] = "E",
                [CharacterCreationPriorityCategoryIds.Talent] = aspectedGroup is null ? "C" : "D",
                [CharacterCreationPriorityCategoryIds.Attributes] = "A",
                [CharacterCreationPriorityCategoryIds.Skills] = "B",
                [CharacterCreationPriorityCategoryIds.Resources] = aspectedGroup is null ? "D" : "C"
            };
            if (buildMethod == CharacterCreationBuildMethods.SumToTen)
            {
                // Repeated ranks: E/C/A/C/C or E/D/A/C/B, both ten points.
                ranks[CharacterCreationPriorityCategoryIds.Skills] = "C";
                ranks[CharacterCreationPriorityCategoryIds.Resources] = aspectedGroup is null ? "C" : "B";
            }
            var heritage = initial.Authority.Options.Single(item => item.CategoryId == CharacterCreationPriorityCategoryIds.Heritage
                && item.Rank == "E").HeritageOptions.First(item => item.IsEnabled && item.MetatypeName == "Human" && item.MetavariantSourceId is null);
            var talent = initial.Authority.Options.Single(item => item.CategoryId == CharacterCreationPriorityCategoryIds.Talent
                && item.Rank == ranks[CharacterCreationPriorityCategoryIds.Talent]).TalentOptions.First(item => item.IsEnabled
                    && (aspectedGroup is not null ? item.Value == "Aspected Magician"
                        : technomancer ? item.Value == "Technomancer" : mysticAdept ? item.Value == "Mystic Adept"
                        : item.Value == "Adept" && item.Magic == 4));
            string[] skills = talent.ActiveSkillGrant?.Options.Where(item => item.IsEnabled)
                .Take(talent.ActiveSkillGrant.Quantity).Select(item => item.SelectionId).ToArray() ?? [];
            string[] groups = [];
            if (aspectedGroup is not null)
            {
                var priorityOverview = PriorityOverview(initial);
                var priorityPhone = new CreationPrerequisitePhoneDraft();
                priorityPhone.Bind(initial, priorityOverview);
                foreach (var rank in ranks)
                    Require(priorityPhone.TrySelect(initial, priorityOverview, rank.Key, rank.Value),
                        $"Phone rejected source Priority {rank.Key}/{rank.Value}; ready={CreationPrerequisitePhoneAuthority.IsReady(initial, priorityOverview)}.");
                Require(priorityPhone.TrySelectHeritage(initial, priorityOverview, heritage.SelectionId), "Phone rejected Human.");
                Require(priorityPhone.TrySelectTalent(initial, priorityOverview, talent.SelectionId),
                    "Phone rejected a mandatory source choice merely because it grants zero skill levels.");
                Require(!priorityPhone.CanPrepare(initial, priorityOverview) && priorityPhone.Selections(initial, priorityOverview) is null,
                    "The phone silently chose an aspect or skipped the mandatory zero-rating prompt.");
                Require(!priorityPhone.TryToggleTalentSkillGroup(initial, priorityOverview, "invented-group"), "Phone accepted an invented aspect.");
                var group = talent.SkillGroupGrant!.Options.Single(item => item.CanonicalName == aspectedGroup);
                Require(talent.SkillGroupGrant.BaseRating == 0 && talent.SkillGroupGrant.Quantity == 1,
                    "The canonical Priority D source must require one choice without free levels.");
                Require(priorityPhone.TryToggleTalentSkillGroup(initial, priorityOverview, group.SelectionId), "Phone rejected the explicit aspect.");
                Require(!priorityPhone.TryToggleTalentSkillGroup(initial, priorityOverview,
                    talent.SkillGroupGrant.Options.First(item => item.SelectionId != group.SelectionId).SelectionId),
                    "The phone accepted more than the source-owned choice quantity.");
                Require(priorityPhone.CanPrepare(initial, priorityOverview), "An explicit zero-rating choice did not complete Priority.");
                groups = priorityPhone.Selections(initial, priorityOverview)!.TalentSkillGroupSelectionIds.ToArray();
                Require(groups.SequenceEqual(new[] { group.SelectionId }), "Phone selection was not source-bound.");
                var omitted = prerequisites.Preview(new(initial.Binding, ranks)
                    { HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talent.SelectionId });
                Require(omitted.Value is not { CanConfirm: true }, "Core skipped a required zero-rating choice.");
                var duplicated = prerequisites.Preview(new(initial.Binding, ranks)
                    { HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talent.SelectionId,
                        TalentSkillGroupSelectionIds = [group.SelectionId, group.SelectionId] });
                Require(duplicated.Value is not { CanConfirm: true }, "Core accepted a repeated source choice.");
            }
            var priority = prerequisites.Preview(new(initial.Binding, ranks)
            { HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talent.SelectionId,
                TalentActiveSkillSelectionIds = skills, TalentSkillGroupSelectionIds = groups }).Value!;
            Require(priority.CanConfirm, string.Join(",", priority.Blockers));
            var selected = prerequisites.Confirm(new(priority.Binding, ranks, priority.PreviewDigest, true)
            { HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talent.SelectionId,
                TalentActiveSkillSelectionIds = skills, TalentSkillGroupSelectionIds = groups });
            Require(selected.Outcome == CharacterCreationFoundationOutcomes.Success, string.Join(",", selected.Blockers));
            if (aspectedGroup is not null)
            {
                var reloadedPriority = new CharacterCreationPrerequisiteService(new FileWorkspaceStore(directory), queries, resolver)
                    .Load(new(id)).Value!;
                var reloadedOverview = PriorityOverview(reloadedPriority);
                var reloadedPhone = new CreationPrerequisitePhoneDraft();
                reloadedPhone.Bind(reloadedPriority, reloadedOverview);
                Require(reloadedPhone.CanPrepare(reloadedPriority, reloadedOverview)
                    && reloadedPhone.TalentSkillGroupSelectionIds(reloadedPriority, reloadedOverview).SequenceEqual(groups),
                    "Cold Priority phone lost the explicit zero-rating selection before dependent drafts exist: "
                    + PriorityDiagnostics(reloadedPriority, reloadedOverview, reloadedPhone));
            }
            var attributes = new CharacterCreationAttributesService(store, resolver);
            var attributeState = attributes.Load(new(id)).Value!;
            CharacterCreationAttributeAllocation[] allocations = [new(technomancer ? "RES" : "MAG", 1, 0)];
            var attributeReview = attributes.Preview(new(attributeState.Binding, allocations)).Value!;
            Require(attributeReview.CanConfirm, string.Join(",", attributeReview.Blockers));
            var assigned = attributes.Confirm(new(attributeReview.Binding, allocations, attributeReview.PreviewDigest, true));
            Require(assigned.Outcome == CharacterCreationFoundationOutcomes.Success, string.Join(",", assigned.Blockers));
            var firstSkillsCommand = RunSkillAccess(store, resolver, id, directory, technomancer, aspectedGroup, contentRoot);
            var service = new CharacterCreationMagicResonanceService(store, resolver);
            var state = service.Load(new(id)).Value!;
            Require(state.CanEdit, string.Join(",", state.Blockers));
            Require(state.PrerequisiteDraft!.BuildMethod == buildMethod,
                "Magic must retain the exact Core creation method rather than relabel Sum-to-Ten as Priority.");
            Require(CharacterCreationMagicResonanceWorkflow.TryProject(state, out _),
                $"Actual {buildMethod} Core state rejected by Presentation: " + ProjectionDiagnostics(state));
            VerifyMissingDraftEntry(state);
            if (seedDirectory is not null)
            {
                foreach (string source in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                {
                    string target = Path.Combine(seedDirectory, Path.GetRelativePath(directory, source));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(source, target, overwrite: false);
                }
                Console.WriteLine($"SEED {id.Value} revision={state.Binding.ContentRevision}/{state.Binding.SavedRevision}");
                return;
            }
            if (aspectedGroup is not null)
            {
                RunAspected(store, resolver, service, state, id, directory, aspectedGroup);
                RunSkillsRevisit(resolver, id, directory, firstSkillsCommand, technomancer);
                return;
            }
            if (technomancer)
            {
                RunTechnomancer(store, resolver, service, state, id, directory);
                RunSkillsRevisit(resolver, id, directory, firstSkillsCommand, technomancer);
                return;
            }
            if (mysticAdept)
            {
                RunMysticAdept(store, resolver, service, state, id, directory, contentRoot);
                RunSkillsRevisit(resolver, id, directory, firstSkillsCommand, technomancer);
                return;
            }
            Require(state.SelectedTalent!.Magic == 4 && state.AdeptPowerPointBudget.Total == 5,
                "Canonical C Adept source MAG must remain 4; confirmed MAG and spend budget must be 5.");
            string sourceTalentDigest = CharacterCreationMagicResonanceDigest.Compute(state.SelectedTalent);
            Require(CharacterCreationMagicResonanceWorkflow.TryProject(state, out var projected),
                "Actual Core state rejected: " + ProjectionDiagnostics(state));
            var editor = projected!;
            var wrongSources = state with { PrerequisiteDraft = state.PrerequisiteDraft! with
                { TalentSelection = state.PrerequisiteDraft!.TalentSelection! with
                    { SourceAnchorIds = state.PrerequisiteDraft.TalentSelection!.SourceAnchorIds.Append("invented-source").ToArray() } } };
            wrongSources = wrongSources with { SnapshotDigest = string.Empty };
            wrongSources = wrongSources with { SnapshotDigest = CharacterCreationMagicResonanceDigest.Compute(wrongSources) };
            Require(!CharacterCreationMagicResonanceWorkflow.TryProject(wrongSources, out _),
                "An invented selection source anchor survived the richer provenance comparison.");
            Require(editor.CanEdit && editor.Budgets.Single(item => item.Kind == CharacterCreationMagicResonanceKinds.AdeptPower).Total == 5,
                "Presentation rejected the actual raised-MAG Core state.");
            var light = editor.AdeptPowers.Single(item => item.Name == "Light Body");
            Require(state.Authority.AdeptPowers.Single(item => item.Identity == light.Identity).MaximumLevels == int.MaxValue
                && light.MaximumLevels == 5 && light.IsEnabled,
                "The phone exposed the source instance limit/unbounded rating instead of current MAG.");
            Require(editor.AdeptPowers.Single(item => item.Name == "Traceless Walk").MaximumLevels == 1,
                "A non-levelled power gained extra ranks.");
            Require(editor.AdeptPowers.Single(item => item.Name == "Improved Reflexes").MaximumLevels == 3,
                "An explicit lower source rating cap was lost.");
            var overview = Program.NewCreationOverview(id, state.Binding.ContentRevision, state.Binding.SavedRevision) with
            { CreationMagicResonance = state, CreationMagicResonanceEditor = editor };
            var phone = new CreationMagicResonancePhoneDraft();
            phone.Bind(editor, overview);
            Require(phone.CreatePowerLevelCandidate(light, 5).Selections.AdeptPowers.Single().Levels == 5,
                "Native selection rejected the legal effective rank.");
            ExpectRejected(() => phone.CreatePowerLevelCandidate(light, 6));
            ExpectRejected(() => phone.CreatePowerLevelCandidate(light with { MaximumLevels = 99 }, 6));
            foreach (decimal forgedBudget in new[] { 4m, 6m })
            {
                var forged = state with { AdeptPowerPointBudget = state.AdeptPowerPointBudget with
                    { Total = forgedBudget, Remaining = forgedBudget }, SnapshotDigest = string.Empty };
                forged = forged with { SnapshotDigest = CharacterCreationMagicResonanceDigest.Compute(forged) };
                Require(!CharacterCreationMagicResonanceWorkflow.TryProject(forged, out _),
                    "Rehashed stale/forged effective budget entered Presentation.");
            }
            // Explicit user choices spend 5 PP at full source cost: 1.25 + 1.25 + 1 + 1 + .5.
            (string Name, int Levels)[] choices = [("Light Body", 5), ("Adrenaline Boost", 5),
                ("Missile Parry", 4), ("Traceless Walk", 1), ("Wall Running", 1)];
            var powers = choices.Select(choice => new CharacterCreationAdeptPowerAllocation(
                    editor.AdeptPowers.Single(item => item.Name == choice.Name).Identity, choice.Levels)).ToArray();
            var draft = CreationMagicResonancePhoneAuthority.CreateDraft(editor, new(null, null, powers, [], []));
            var review = CharacterCreationMagicResonanceWorkflow.Review(service, editor, draft);
            var beforePrepared = phone.Copy();
            var preparedPhone = beforePrepared.Copy();
            Require(preparedPhone.TryAdopt(editor, overview, review)
                && phone.TryAdoptPrepared(beforePrepared, preparedPhone), "Background review was not adopted.");
            Require(!phone.TryAdoptPrepared(beforePrepared, beforePrepared), "A late worker overwrote a newer local choice.");
            Require(phone.Review == review, "Rejected stale copy changed the current draft.");
            Require(review.Preview.CanConfirm && phone.TryAdopt(editor, overview, review), "Native review lost exact Core budget authority.");
            AfterRunAuthorityHarness.RunCreationMagicBackgroundAsync(contentRoot, directory, id, draft)
                .GetAwaiter().GetResult();
            string beforeXml = store.Get(id).Value!.Document.Content;
            string key = CreationMagicResonancePhoneAuthority.ComputeIdempotencyKey(review);
            var confirmed = CharacterCreationMagicResonanceWorkflow.Confirm(service, review, key, explicitlyConfirmed: true);
            VerifyReceiptHydration(review, confirmed);
            var coldStore = new FileWorkspaceStore(directory);
            var coldService = new CharacterCreationMagicResonanceService(coldStore, resolver);
            var cold = coldService.Load(new(id)).Value!;
            var reopened = CharacterCreationMagicResonanceWorkflow.Project(cold);
            Require(reopened.CanEdit && reopened.AdeptPowers.Single(item => item.Identity == light.Identity).MaximumLevels == 5
                && reopened.Budgets.Single(item => item.Kind == CharacterCreationMagicResonanceKinds.AdeptPower).Remaining == 0,
                "Cold file-store/native projection lost the confirmed budget or rating cap.");
            Require(CharacterCreationMagicResonanceDigest.Compute(cold.SelectedTalent!) == sourceTalentDigest
                && coldStore.Get(id).Value!.Document.Content == beforeXml,
                "Wizard confirmation rewrote source Talent or applied character effects before finalization.");
            var replay = CharacterCreationMagicResonanceWorkflow.Confirm(coldService, review, key, explicitlyConfirmed: true);
            Require(replay.Receipt.ReceiptDigest == confirmed.Receipt.ReceiptDigest
                && coldStore.Get(id).Value!.ContentRevision == confirmed.Receipt.ContentRevision, "Retry persisted another mutation.");
            phone.Bind(editor, Program.NewCreationOverview(id, cold.Binding.ContentRevision, cold.Binding.SavedRevision) with
                { CreationMagicResonance = state, CreationMagicResonanceEditor = editor });
            ExpectRejected(() => phone.CreatePowerLevelCandidate(light, 4));
            Console.WriteLine("PASS actual Adept source/raised MAG → Presentation caps → phone review/confirm → cold file-store reopen/replay");
            if (magicReReview)
            {
                AfterRunAuthorityHarness.RunMagicReReviewCasesAsync(contentRoot, directory, resolver, id)
                    .GetAwaiter().GetResult();
                return;
            }
            RunSkillsRevisit(resolver, id, directory, firstSkillsCommand, technomancer);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void VerifyMissingDraftEntry(CharacterCreationMagicResonanceState state)
    {
        var editor = CharacterCreationMagicResonanceWorkflow.Project(state);
        var overview = Program.NewCreationOverview(state.Binding.WorkspaceId,
            state.Binding.ContentRevision, state.Binding.SavedRevision) with
        { CreationMagicResonance = state, CreationMagicResonanceEditor = editor };
        var stage = new CharacterCreationWizardStageState(
            CharacterCreationWizardStepIds.MagicResonance, "Magic", CharacterCreationWizardStepStatuses.Blocked,
            IsRequired: true, IsAvailable: false, IsComplete: false, [],
            [CharacterCreationFinalizationBlockers.MagicResonanceDraftRequired], [], []);
        bool ready = CreationMagicResonancePhoneAuthority.IsReady(state, editor, overview);
        var preparedDraft = new CreationMagicResonancePhoneDraft();
        Require(preparedDraft.TryBindLoaded(state, overview, out var bound)
            && bound is not null && preparedDraft.Matches(bound, overview),
            "Fresh draft admission did not preserve the full existing readiness contract.");
        var beforeInvalid = preparedDraft.Selections;
        Require(CreationMagicResonancePhoneAuthority.TryProjectForOverview(state, overview, out var prepared)
            && prepared is not null && CreationMagicResonancePhoneAuthority.EditorEquals(editor, prepared),
            "Single-pass admission must return the exact canonical projection.");
        Require(!CreationMagicResonancePhoneAuthority.TryProjectForOverview(
                state with { SnapshotDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8("tampered") },
                overview, out var corrupt) && corrupt is null,
            "Single-pass admission exposed a projection for corrupt Core state.");
        var changedEditor = editor with { Talent = editor.Talent with { Name = "invented talent" } };
        Require(!preparedDraft.TryBindLoaded(
                state with { SnapshotDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8("tampered") },
                overview, out var invalidBinding) && invalidBinding is null
            && !preparedDraft.TryBindLoaded(state,
                overview with { CreationMagicResonanceEditor = changedEditor }, out invalidBinding)
            && invalidBinding is null && ReferenceEquals(beforeInvalid, preparedDraft.Selections),
            "Invalid fresh binding exposed an editor or overwrote the retained draft.");
        Require(!CreationMagicResonancePhoneAuthority.IsReady(state, changedEditor, overview)
            && !CreationMagicResonancePhoneAuthority.TryProjectForOverview(state,
                overview with { CreationMagicResonanceEditor = changedEditor }, out var changed)
            && changed is null,
            "Single-pass admission accepted a non-canonical displayed editor.");
        Require(ready && BuildPageUiProjection.CanOpenExactTypedCreationStage(
            stage, CharacterCreationWizardStepIds.MagicResonance, ready),
            "The real source-bound Magic editor could not author its own required draft.");
        var available = stage with { IsAvailable = true, Status = CharacterCreationWizardStepStatuses.InProgress };
        Require(BuildPageUiProjection.CanOpenExactTypedCreationStage(
            available, CharacterCreationWizardStepIds.MagicResonance, ready),
            "An available Magic editor was blocked by its own missing finalization draft.");
        Require(!BuildPageUiProjection.CanOpenExactTypedCreationStage(
            available with { Blockers = [CharacterCreationFinalizationBlockers.SkillsDraftRequired] },
            CharacterCreationWizardStepIds.MagicResonance, ready)
            && !BuildPageUiProjection.CanOpenExactTypedCreationStage(
                available with { Blockers = [CharacterCreationFinalizationBlockers.MagicResonanceDraftRequired,
                    CharacterCreationFinalizationBlockers.SkillsDraftRequired] },
                CharacterCreationWizardStepIds.MagicResonance, ready),
            "Available Magic entry ignored a different or additional prerequisite.");
        foreach (var stale in new[]
        {
            overview with { CreationMagicResonance = null },
            overview with { CreationMagicResonanceEditor = null },
            Program.NewCreationOverview(state.Binding.WorkspaceId,
                state.Binding.ContentRevision + 1, state.Binding.SavedRevision + 1) with
            { CreationMagicResonance = state, CreationMagicResonanceEditor = editor },
            overview with { Error = "source-unavailable" }
        })
        {
            bool staleReady = CreationMagicResonancePhoneAuthority.IsReady(state, editor, stale);
            Require(!preparedDraft.TryBindLoaded(state, stale, out var staleBinding)
                && staleBinding is null && ReferenceEquals(beforeInvalid, preparedDraft.Selections),
                "Fresh binding accepted stale authority or silently replaced the local choices.");
            Require(!CreationMagicResonancePhoneAuthority.TryProjectForOverview(state, stale, out var rejected)
                && rejected is null, "Rejected Magic authority must not expose a usable editor.");
            Require(!staleReady && !BuildPageUiProjection.CanOpenExactTypedCreationStage(
                stage, CharacterCreationWizardStepIds.MagicResonance, staleReady),
                "Missing-draft entry admitted stale or absent Magic authority.");
        }
        Require(!stage.IsAvailable && !stage.IsComplete
            && stage.Blockers.SequenceEqual([CharacterCreationFinalizationBlockers.MagicResonanceDraftRequired]),
            "Editor entry changed finalization evidence.");
    }

    private static CharacterCreationSkillsConfirmRequest RunSkillAccess(FileWorkspaceStore store, FileSystemCharacterSourceDataResolver resolver,
        CharacterWorkspaceId id, string directory, bool technomancer, string? aspect, string contentRoot)
    {
        var service = new CharacterCreationSkillsService(store, resolver);
        var state = service.Load(new(id)).Value!;
        var overview = Program.NewCreationOverview(id, state.Binding.ContentRevision, state.Binding.SavedRevision);
        Require(CreationSkillsPhoneAuthority.IsReady(state, overview), "Source-bound Skills unavailable: " + string.Join(",", state.Blockers));
        var phone = new CreationSkillsPhoneDraft();
        phone.Bind(state, overview);
        var forbidden = state.Authority.ActiveSkills.First(item => item.Category == (technomancer ? "Magical Active" : "Resonance Active"));
        var forbiddenGroup = state.Authority.SkillGroups.Single(item => item.Name == (technomancer ? "Sorcery" : "Tasking"));
        Require(!CreationSkillsPhoneAuthority.AvailableActiveSkills(state).Any(item => item.SourceSkillId == forbidden.SourceSkillId)
            && !CreationSkillsPhoneAuthority.AvailableGroups(state).Any(item => item.GroupId == forbiddenGroup.GroupId),
            "The native picker exposed a skill/group outside Core's source permissions.");
        Require(phone.WithSkill(forbidden, 1).SequenceEqual(phone.Skills)
            && phone.WithGroup(forbiddenGroup, 1).SequenceEqual(phone.Groups),
            "The native draft accepted a hidden, unauthorized catalog entry.");
        if (aspect is not null)
        {
            foreach (var group in state.Authority.SkillGroups.Where(item => item.Name is "Sorcery" or "Conjuring" or "Enchanting"))
                Require(CreationSkillsPhoneAuthority.AvailableGroups(state).Any(item => item.GroupId == group.GroupId) == (group.Name == aspect),
                    "The Skills picker did not preserve the selected Aspected Priority D group.");
        }
        var language = state.Authority.KnowledgeSkills.First(item => item.CanBeNativeLanguage);
        var allowed = state.Authority.ActiveSkills.Single(item => item.Name == (technomancer ? "Compiling" : "Arcana"));
        CharacterCreationSkillAllocation[] illegal =
            [new(language.SourceSkillId, CharacterCreationSkillKinds.Knowledge, null, null, true),
                new(forbidden.SourceSkillId, CharacterCreationSkillKinds.Active, 1, null, false)];
        var rejected = service.Preview(new(state.Binding, illegal, phone.Groups));
        Require(!phone.TryAdopt(state, overview, rejected, illegal, phone.Groups),
            "The phone adopted a rejected Core skill purchase.");
        var access = state.Authority.TalentAccess!;
        var forgedAccess = access with { AllowedActiveSkillSourceIds = access.AllowedActiveSkillSourceIds.Append(forbidden.SourceSkillId)
            .OrderBy(item => item, StringComparer.Ordinal).ToArray(), AccessDigest = string.Empty };
        forgedAccess = forgedAccess with { AccessDigest = CharacterCreationSkillsDigest.Compute(forgedAccess) };
        var forgedAuthority = state.Authority with { TalentAccess = forgedAccess, AuthorityDigest = string.Empty };
        forgedAuthority = forgedAuthority with { AuthorityDigest = CharacterCreationSkillsDigest.Compute(forgedAuthority) };
        var forgedState = state with { Authority = forgedAuthority,
            Binding = state.Binding with { SkillsAuthorityDigest = forgedAuthority.AuthorityDigest }, SnapshotDigest = string.Empty };
        forgedState = forgedState with { SnapshotDigest = CharacterCreationSkillsDigest.Compute(forgedState) };
        Require(!CreationSkillsPhoneAuthority.IsReady(forgedState, overview),
            "The phone accepted a rehashed skill-permission list inconsistent with source effects.");
        void Adopt(IReadOnlyList<CharacterCreationSkillAllocation> skills, IReadOnlyList<CharacterCreationSkillGroupAllocation> groups)
        {
            var preview = service.Preview(new(state.Binding, skills, groups));
            Require(phone.TryAdopt(state, overview, preview, skills, groups), "Native rejected legal Core Skills preview: " + string.Join(",", preview.Blockers));
        }
        int expectedRating = (phone.Skills.SingleOrDefault(item => item.SourceSkillId == allowed.SourceSkillId)?.Rating ?? 0) + 1;
        Adopt(phone.WithSkill(allowed, 1), phone.Groups);
        if (aspect is not null)
            Adopt(phone.Skills, phone.WithGroup(state.Authority.SkillGroups.Single(item => item.Name == aspect), 1));
        var incomplete = phone.Preview!;
        Require(!incomplete.CanConfirm
            && incomplete.Blockers.SequenceEqual(new[] { CharacterCreationSkillsBlockers.NativeLanguageRequired })
            && phone.Skills.Any(item => item.SourceSkillId == allowed.SourceSkillId && item.Rating == expectedRating),
            "A valid skill/group edit disappeared while the native-language choice was incomplete.");
        var incompleteResult = service.Preview(new(state.Binding, phone.Skills, phone.Groups));
        Require(!CreationSkillsPhoneAuthority.CanAdoptPreview(state, overview, incompleteResult, phone.Skills, phone.Groups)
            && !CreationSkillsPhoneAuthority.CanConfirmPreview(state, overview, incomplete, phone.Skills, phone.Groups),
            "An incomplete local selection became review/save authority.");
        long beforeIncomplete = store.Get(id).Value!.ContentRevision;
        Require(service.Confirm(new(incomplete.Binding, phone.Skills, phone.Groups, incomplete.PreviewDigest,
                "incomplete-native-language", ExplicitlyConfirmed: true)).Value is null
            && store.Get(id).Value!.ContentRevision == beforeIncomplete,
            "Incomplete Skills confirmation wrote a workspace mutation.");
        var preservedSkills = phone.Skills.ToArray();
        var preservedGroups = phone.Groups.ToArray();
        var stale = incomplete with { Binding = incomplete.Binding with { ContentRevision = incomplete.Binding.ContentRevision + 1 } };
        stale = stale with { PreviewDigest = CharacterCreationSkillsDigest.Compute(stale with { PreviewDigest = string.Empty }) };
        foreach (var invalid in new[]
        {
            incompleteResult with { Outcome = CharacterCreationFoundationOutcomes.Conflict },
            incompleteResult with { Blockers = [] },
            incompleteResult with { Value = incomplete with { CanConfirm = true } },
            incompleteResult with { Value = incomplete with { PreviewDigest = new string('0', 64) } },
            incompleteResult with { Value = stale }
        })
            Require(!phone.TryAdopt(state, overview, invalid, phone.Skills, phone.Groups),
                "Local staging accepted stale, tampered, over-budget or unauthorized Skills evidence.");
        foreach (var badSkills in new[] { phone.WithSkill(allowed, 100),
                     illegal.Where(item => !item.IsNativeLanguage).ToArray() })
            Require(!phone.TryAdopt(state, overview, service.Preview(new(state.Binding, badSkills, phone.Groups)),
                    badSkills, phone.Groups), "Local staging accepted Core-rejected costs or talent access.");
        Require(!phone.TryAdopt(state, overview, incompleteResult, [], phone.Groups)
            && !phone.TryAdopt(state, Program.NewCreationOverview(id, overview.ContentRevision + 1, overview.SavedRevision),
                incompleteResult, phone.Skills, phone.Groups)
            && phone.Skills.SequenceEqual(preservedSkills) && phone.Groups.SequenceEqual(preservedGroups),
            "A rejected selection or changed workspace modified the local draft.");
        Adopt(phone.WithSkill(language, 0, native: true), phone.Groups);
        Require(phone.Preview!.CanConfirm, "Native language did not complete the retained selections.");
        Adopt(phone.Skills.Where(item => !item.IsNativeLanguage).ToArray(), phone.Groups);
        Require(!phone.Preview!.CanConfirm && phone.Skills.SequenceEqual(preservedSkills),
            "Removing the native language lost other local selections or permitted saving.");
        Adopt(phone.WithSkill(language, 0, native: true), phone.Groups);
        var review = phone.Preview!;
        var command = new CharacterCreationSkillsConfirmRequest(review.Binding, phone.Skills, phone.Groups,
            review.PreviewDigest, "native-talent-skill-access", ExplicitlyConfirmed: true);
        var saved = service.Confirm(command);
        Require(saved.Outcome == CharacterCreationFoundationOutcomes.Success, string.Join(",", saved.Blockers));
        if (aspect == "Sorcery")
            AfterRunAuthorityHarness.RunSkillsReReviewCasesAsync(contentRoot, directory, resolver, id, state, review, saved.Value!)
                .GetAwaiter().GetResult();
        var coldStore = new FileWorkspaceStore(directory);
        var coldService = new CharacterCreationSkillsService(coldStore, resolver);
        var cold = coldService.Load(new(id)).Value!;
        var coldOverview = Program.NewCreationOverview(id, cold.Binding.ContentRevision, cold.Binding.SavedRevision);
        var coldPhone = new CreationSkillsPhoneDraft();
        coldPhone.Bind(cold, coldOverview);
        Require(coldPhone.Matches(cold, coldOverview) && coldPhone.Skills.SequenceEqual(phone.Skills)
            && coldPhone.Groups.SequenceEqual(phone.Groups), "Cold phone lost legal skill purchases or source access.");
        long revision = coldStore.Get(id).Value!.ContentRevision;
        Require(coldService.Confirm(command).Value!.ReceiptDigest == saved.Value!.ReceiptDigest
            && coldStore.Get(id).Value!.ContentRevision == revision, "Skill replay wrote a second mutation.");
        Require(!CreationSkillsPhoneAuthority.IsReady(cold, overview), "Phone accepted a previous workspace revision.");
        Console.WriteLine("PASS actual source skill access → native picker/review → confirmed save → cold reopen/replay");
        return command;
    }

    private static void RunSkillsRevisit(FileSystemCharacterSourceDataResolver resolver, CharacterWorkspaceId id,
        string directory, CharacterCreationSkillsConfirmRequest firstCommand, bool technomancer)
    {
        var store = new FileWorkspaceStore(directory);
        var before = store.Get(id).Value!;
        var firstReceipt = before.Document.AuxiliaryState.CharacterCreationSkillsReceipts!.Single();
        var magicDraft = before.Document.AuxiliaryState.CharacterCreationMagicResonanceDraft!;
        var magicReceipts = before.Document.AuxiliaryState.CharacterCreationMagicResonanceReceipts!;
        Require(before.ContentRevision > firstReceipt.ContentRevision && magicReceipts.Count > 0,
            "The actual Magic wizard must have saved between Skills visits.");
        var service = new CharacterCreationSkillsService(store, resolver);
        var state = service.Load(new(id)).Value!;
        var overview = Program.NewCreationOverview(id, state.Binding.ContentRevision, state.Binding.SavedRevision);
        var phone = new CreationSkillsPhoneDraft();
        phone.Bind(state, overview);
        Require(phone.Matches(state, overview), "Skills became unavailable after Magic: " + string.Join(",", state.Blockers));
        Require(!CreationSkillsPhoneAuthority.IsReady(state,
            Program.NewCreationOverview(id, firstCommand.Binding.ContentRevision, firstCommand.Binding.SavedRevision)),
            "Returning to Skills accepted the old host revision.");
        var source = CreationSkillsPhoneAuthority.AvailableActiveSkills(state)
            .Single(item => item.Name == (technomancer ? "Compiling" : "Arcana"));
        var choices = phone.WithSkill(source, 1);
        var preview = service.Preview(new(state.Binding, choices, phone.Groups));
        Require(phone.TryAdopt(state, overview, preview, choices, phone.Groups),
            "The revisited phone rejected a legal Skills increase: " + string.Join(",", preview.Blockers));
        var review = phone.Preview!;
        var command = new CharacterCreationSkillsConfirmRequest(review.Binding, phone.Skills, phone.Groups,
            review.PreviewDigest, "native-skills-after-magic", ExplicitlyConfirmed: true);
        Require(service.Confirm(command with { ExplicitlyConfirmed = false }).Outcome != CharacterCreationFoundationOutcomes.Success,
            "Revisiting Skills bypassed explicit review.");
        Require(store.Get(id).Value!.Document.AuxiliaryStateDigest == before.Document.AuxiliaryStateDigest,
            "Preview or rejected confirmation changed the saved wizard data.");
        var result = service.Confirm(command);
        Require(result.Outcome == CharacterCreationFoundationOutcomes.Success,
            "Skills confirmation rejected a legitimate inter-wizard revision gap: " + string.Join(",", result.Blockers));
        var receipt = result.Value!;
        Require(receipt.PreviousContentRevision == before.ContentRevision
            && receipt.ContentRevision == before.ContentRevision + 1
            && receipt.DraftRevision == firstReceipt.DraftRevision + 1
            && receipt.PreviousReceiptDigest == firstReceipt.ReceiptDigest,
            "The new Skills save did not extend its own receipt chain at the current workspace revision.");

        var coldStore = new FileWorkspaceStore(directory);
        var coldService = new CharacterCreationSkillsService(coldStore, resolver);
        var cold = coldService.Load(new(id)).Value!;
        var coldOverview = Program.NewCreationOverview(id, cold.Binding.ContentRevision, cold.Binding.SavedRevision);
        var coldPhone = new CreationSkillsPhoneDraft();
        coldPhone.Bind(cold, coldOverview);
        Require(coldPhone.Matches(cold, coldOverview) && coldPhone.Skills.SequenceEqual(phone.Skills)
            && coldPhone.Groups.SequenceEqual(phone.Groups), "Cold phone lost revisited Skills choices.");
        Require(coldService.Confirm(firstCommand).Value!.ReceiptDigest == firstReceipt.ReceiptDigest
            && coldService.Confirm(command).Value!.ReceiptDigest == receipt.ReceiptDigest,
            "Exact replay did not preserve the old and new Skills receipts.");
        Require(coldService.Confirm(firstCommand with { Allocations = command.Allocations }).Outcome
            == CharacterCreationFoundationOutcomes.Conflict, "An old command key accepted changed choices.");
        Require(coldService.Confirm(firstCommand with { IdempotencyKey = "native-stale-revisit" }).Outcome
            != CharacterCreationFoundationOutcomes.Success, "A new command accepted the stale pre-Magic binding.");
        var after = coldStore.Get(id).Value!;
        Require(after.ContentRevision == receipt.ContentRevision && after.SavedRevision == receipt.SavedRevision
            && after.Document.Content == before.Document.Content
            && after.Document.AuxiliaryState.CharacterCreationSkillsReceipts is { Count: 2 }
            && after.Document.AuxiliaryState.CharacterCreationMagicResonanceDraft!.DraftDigest == magicDraft.DraftDigest
            && after.Document.AuxiliaryState.CharacterCreationMagicResonanceReceipts!.Select(item => item.ReceiptDigest)
                .SequenceEqual(magicReceipts.Select(item => item.ReceiptDigest)),
            "Skills revisit or replay rewrote Magic, applied character effects or saved another revision.");
        var magic = new CharacterCreationMagicResonanceService(coldStore, resolver).Load(new(id)).Value!;
        Require(CharacterCreationMagicResonanceWorkflow.TryProject(magic, out var editor) && editor!.CanEdit,
            "Returning to Skills invalidated the saved Magic projection.");
        var magicPhone = new CreationMagicResonancePhoneDraft();
        var magicOverview = coldOverview with { CreationMagicResonance = magic, CreationMagicResonanceEditor = editor };
        magicPhone.Bind(editor!, magicOverview);
        Require(magicPhone.Matches(editor!, magicOverview)
            && CharacterCreationMagicResonanceDigest.Compute(magicPhone.Selections)
                == CharacterCreationMagicResonanceDigest.Compute(editor!.Selections),
            "The cold Magic phone did not retain its saved choices after the Skills revisit.");
        Require(after.Document.AuxiliaryStateDigest == coldStore.Get(id).Value!.Document.AuxiliaryStateDigest,
            "Cold projection or phone binding persisted data.");
        Console.WriteLine("PASS actual Skills → Magic → Skills phone revisit → cold reopen → both receipts replay without another write");
    }

    private static void RunAspected(FileWorkspaceStore store, FileSystemCharacterSourceDataResolver resolver,
        CharacterCreationMagicResonanceService service, CharacterCreationMagicResonanceState state,
        CharacterWorkspaceId id, string directory, string aspect)
    {
        Require(CharacterCreationMagicResonanceWorkflow.TryProject(state, out var projection),
            "Aspected Core state rejected: " + ProjectionDiagnostics(state));
        var editor = projection!;
        var overview = Program.NewCreationOverview(id, state.Binding.ContentRevision, state.Binding.SavedRevision) with
            { CreationMagicResonance = state, CreationMagicResonanceEditor = editor };
        var phone = new CreationMagicResonancePhoneDraft();
        phone.Bind(editor, overview);
        var tradition = editor.Traditions.First(item => item.IsEnabled);
        var review = CharacterCreationMagicResonanceWorkflow.Review(service, editor, phone.CreateSingleCandidate(tradition));
        Require(review.Preview.CanConfirm && phone.TryAdopt(editor, overview, review),
            "Phone lost the Aspected tradition or required an unrelated selection: " + string.Join(",", review.Preview.Blockers));
        string beforeXml = store.Get(id).Value!.Document.Content;
        string key = CreationMagicResonancePhoneAuthority.ComputeIdempotencyKey(review);
        var confirmed = CharacterCreationMagicResonanceWorkflow.Confirm(service, review, key, explicitlyConfirmed: true);
        var coldStore = new FileWorkspaceStore(directory);
        var coldService = new CharacterCreationMagicResonanceService(coldStore, resolver);
        var cold = coldService.Load(new(id)).Value!;
        var reopened = CharacterCreationMagicResonanceWorkflow.Project(cold);
        Require(reopened.CanEdit && reopened.Selections.Tradition == tradition.Identity,
            "Cold native projection lost the user's tradition.");
        var coldPrerequisites = new CharacterCreationPrerequisiteService(coldStore,
            new XmlCharacterFileQueries(new CharacterFileService()), resolver).Load(new(id)).Value!;
        var coldOverview = PriorityOverview(coldPrerequisites);
        var coldPhone = new CreationPrerequisitePhoneDraft();
        coldPhone.Bind(coldPrerequisites, coldOverview);
        var chosen = coldPrerequisites.PendingDraft!.TalentSelection!.GrantPlan!.SkillGroups.Single();
        // After Attributes is confirmed, Core intentionally locks Priority edits.
        // Keep the saved choice and the lock; never forge an editable snapshot.
        Require(chosen.CanonicalName == aspect && chosen.BaseRating == 0
            && coldPrerequisites.Blockers.Contains(CharacterCreationPrerequisiteBlockers.DependentAttributesDraftExists)
            && !coldPhone.CanPrepare(coldPrerequisites, coldOverview),
            "Cold Priority lost the saved aspect or its dependent-draft lock: " + PriorityDiagnostics(coldPrerequisites, coldOverview, coldPhone));
        Require(coldStore.Get(id).Value!.Document.Content == beforeXml,
            "A wizard step applied character effects before finalization.");
        var replay = CharacterCreationMagicResonanceWorkflow.Confirm(coldService, review, key, explicitlyConfirmed: true);
        Require(replay.Receipt.ReceiptDigest == confirmed.Receipt.ReceiptDigest
            && coldStore.Get(id).Value!.ContentRevision == confirmed.Receipt.ContentRevision,
            "Retry applied the Aspected draft twice.");
        Console.WriteLine($"PASS actual Aspected Priority D/{aspect} → mandatory selection-only phone choice → tradition → cold reopen/replay");
    }

    // The test supplies host navigation context, not a second rule authority.
    // Every revision/source/selection below comes from the actual Core load.
    private static CharacterOverviewState PriorityOverview(CharacterCreationPrerequisiteState state)
        => Program.NewCreationOverview(state.Binding.WorkspaceId, state.Binding.ContentRevision, state.Binding.SavedRevision) with
        {
            CreationWizard = new CharacterCreationWizardSnapshot(
                CharacterCreationWizardSchemas.SnapshotV1, state.Binding.WorkspaceId.Value,
                state.Binding.ContentRevision, state.Binding.RawCharacterXmlDigest, state.Binding.AuthorityDigest,
                state.RulesetId, string.Empty, state.BuildMethod, state.CharacterCreated,
                CharacterCreationWizardStepIds.Foundation, [], [],
                new Dictionary<string, IReadOnlyList<CharacterCreationLegalOption>>(), [], [], false, state.SnapshotDigest)
        };

    private static string PriorityDiagnostics(CharacterCreationPrerequisiteState state, CharacterOverviewState overview,
        CreationPrerequisitePhoneDraft phone)
    {
        var pending = state.PendingDraft!;
        var talentRank = state.Authority.Options.Single(item => item.CategoryId == CharacterCreationPriorityCategoryIds.Talent
            && item.Rank == pending.Assignments.Single(assignment => assignment.CategoryId == CharacterCreationPriorityCategoryIds.Talent).Rank);
        var talent = talentRank.TalentOptions.Single(item => item.SelectionId == pending.TalentSelection!.SelectionId);
        var heritageRank = state.Authority.Options.Single(item => item.CategoryId == CharacterCreationPriorityCategoryIds.Heritage
            && item.Rank == pending.Assignments.Single(assignment => assignment.CategoryId == CharacterCreationPriorityCategoryIds.Heritage).Rank);
        var heritage = heritageRank.HeritageOptions.Single(item => item.SelectionId == pending.HeritageSelection!.SelectionId);
        return $"ready={CreationPrerequisitePhoneAuthority.IsReady(state, overview)}; "
            + $"assignments={phone.Assignments(state, overview).Count}; "
            + $"talent={CreationPrerequisitePhoneAuthority.TalentSelectionMatchesOption(pending.TalentSelection!, talent, talentRank.SourceId)}; "
            + $"heritage={CreationPrerequisitePhoneAuthority.HeritageSelectionMatchesOption(pending.HeritageSelection!, heritage, heritageRank.SourceId)}; "
            + $"karma={pending.CreationKarmaUsed}/{state.CreationKarmaBudget.Used}; "
            + $"attributes={pending.EffectiveNormalAttributePoints}/{state.EffectiveNormalAttributePoints}; "
            + $"special={pending.TotalSpecialAttributePoints}/{state.TotalSpecialAttributePoints}; "
            + $"draftAuthority={pending.AuthorityDigest == state.Binding.AuthorityDigest}; blockers={string.Join(',', state.Blockers)}";
    }

    private static void RunTechnomancer(FileWorkspaceStore store, FileSystemCharacterSourceDataResolver resolver,
        CharacterCreationMagicResonanceService service, CharacterCreationMagicResonanceState state,
        CharacterWorkspaceId id, string directory)
    {
        Require(CharacterCreationMagicResonanceWorkflow.TryProject(state, out var projection),
            "Technomancer Core state rejected: " + ProjectionDiagnostics(state));
        var editor = projection!;
        var overview = Program.NewCreationOverview(id, state.Binding.ContentRevision, state.Binding.SavedRevision) with
            { CreationMagicResonance = state, CreationMagicResonanceEditor = editor };
        var phone = new CreationMagicResonancePhoneDraft();
        phone.Bind(editor, overview);
        var stream = editor.Streams.Single(item => item.Name == "Default");
        var review = CharacterCreationMagicResonanceWorkflow.Review(service, editor, phone.CreateSingleCandidate(stream));
        Require(phone.TryAdopt(editor, overview, review), "Phone lost the user-selected stream.");
        var forms = editor.ComplexForms.Where(item => item.IsEnabled).Take(state.SelectedTalent!.ComplexFormBudget).ToArray();
        foreach (var form in forms)
        {
            review = CharacterCreationMagicResonanceWorkflow.Review(service, editor, phone.CreateToggleCandidate(form));
            Require(phone.TryAdopt(editor, overview, review), "Phone lost the selected Complex Form.");
        }
        Require(review.Preview.CanConfirm, string.Join(",", review.Preview.Blockers));
        string beforeXml = store.Get(id).Value!.Document.Content;
        string key = CreationMagicResonancePhoneAuthority.ComputeIdempotencyKey(review);
        var confirmed = CharacterCreationMagicResonanceWorkflow.Confirm(service, review, key, explicitlyConfirmed: true);
        var coldStore = new FileWorkspaceStore(directory);
        var coldService = new CharacterCreationMagicResonanceService(coldStore, resolver);
        var cold = coldService.Load(new(id)).Value!;
        var reopened = CharacterCreationMagicResonanceWorkflow.Project(cold);
        Require(reopened.CanEdit && reopened.Selections.Stream == stream.Identity
            && reopened.Selections.ComplexForms.SequenceEqual(forms.Select(item => item.Identity)),
            "Cold phone projection lost the user's stream/forms.");
        var contribution = coldStore.Get(id).Value!.Document.AuxiliaryState.CharacterCreationMagicResonanceDraft!.FinalizationContribution!;
        var quality = contribution.Talent.GrantedQualitySources!.Single();
        Require(quality.GrantedGearSources!.Single().Name == "Living Persona"
            && contribution.EffectiveAttributes!.Resonance == state.SelectedTalent.Resonance + 1,
            "Source-defined gear or confirmed RES was lost in the actual phone confirmation.");
        Require(CharacterCreationMagicResonanceDigest.Compute(cold.SelectedTalent!)
                == CharacterCreationMagicResonanceDigest.Compute(state.SelectedTalent)
            && coldStore.Get(id).Value!.Document.Content == beforeXml,
            "Phone step changed source grants or applied effects before finalization.");
        var replay = CharacterCreationMagicResonanceWorkflow.Confirm(coldService, review, key, explicitlyConfirmed: true);
        Require(replay.Receipt.ReceiptDigest == confirmed.Receipt.ReceiptDigest
            && coldStore.Get(id).Value!.ContentRevision == confirmed.Receipt.ContentRevision,
            "Retry applied the Technomancer choices twice.");
        // Display labels are not command authority; only the source identity is
        // carried into the draft. Replacing a label must not rename a stream.
        Require(CharacterCreationMagicResonanceDigest.Compute(phone.CreateSingleCandidate(stream))
                == CharacterCreationMagicResonanceDigest.Compute(phone.CreateSingleCandidate(stream with { Name = "invented stream" })),
            "Display-only input changed the typed source selection.");
        ExpectRejected(() => phone.CreateSingleCandidate(stream with
            { Identity = stream.Identity with { SourceId = Guid.NewGuid().ToString("D") } }));
        Console.WriteLine("PASS actual Technomancer source/raised RES → native stream/forms choices → cold file-store reopen/replay");
    }

    private static void RunMysticAdept(FileWorkspaceStore store, FileSystemCharacterSourceDataResolver resolver,
        CharacterCreationMagicResonanceService service, CharacterCreationMagicResonanceState state,
        CharacterWorkspaceId id, string directory, string contentRoot)
    {
        foreach (var (language, title) in new[]
        {
            ("en-GB", "Mystic Adept power points"), ("de-AT", "Kraftpunkte für Mystische Adepten"),
            ("es-MX", "Puntos de poder del adepto místico")
        })
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo(language);
            Require(CreationFlowStrings.Get("Magic.Mystic.Title", "missing", culture) == title,
                "Real Creation resources did not resolve the phone region's language: " + language);
            foreach (string resourceKey in new[] { "Summary", "Boundary", "Decrease", "Increase", "SeparateAttributeUnsupported" })
                Require(CreationFlowStrings.Get("Magic.Mystic." + resourceKey, "missing", culture) != "missing", resourceKey);
            Require(CreationFlowStrings.Format(culture, "Magic.Mystic.Summary", "missing", 2, 4, 10, 0, 5).Contains("10", StringComparison.Ordinal),
                "The translated Core quote did not render.");
        }
        Require(!CreationMagicResonancePage.HasUnsupportedSeparateMagicProfile(state),
            "An ordinary Mystic Adept purchase must not show the separate-attribute notice.");
        var unsupportedProfile = state with
        {
            Authority = state.Authority with
            {
                MysticAdeptPowerPointPolicy = state.Authority.MysticAdeptPowerPointPolicy! with { UsesSeparateMagicAttribute = true }
            },
            SelectedTalent = state.SelectedTalent! with { IsEnabled = false },
            Blockers = [CharacterCreationMagicResonanceBlockers.PowerBudgetUnsupported]
        };
        // Display-only predicate: the forged fixture is never offered to Core or persisted.
        Require(CreationMagicResonancePage.HasUnsupportedSeparateMagicProfile(unsupportedProfile),
            "A blocked separate-attribute profile needs an explanatory notice.");
        Require(!CreationMagicResonancePage.HasUnsupportedSeparateMagicProfile(unsupportedProfile with { Blockers = [] })
            && !CreationMagicResonancePage.HasUnsupportedSeparateMagicProfile(unsupportedProfile with
            { SelectedTalent = state.SelectedTalent! with { Kind = CharacterCreationMagicResonanceKinds.Adept, IsEnabled = false } }),
            "Unrelated blocked talents must not be described as unsupported separate Magic.");
        Require(CharacterCreationMagicResonanceWorkflow.TryProject(state, out var projected),
            "Mystic Adept Core state rejected: " + ProjectionDiagnostics(state));
        foreach (var anchors in new[]
        {
            state.SelectedTalent!.SourceAnchorIds.Append("settings.xml#invented-free-power-points").ToArray(),
            state.SelectedTalent!.SourceAnchorIds.Except(state.Authority.MysticAdeptPowerPointPolicy!.SourceAnchorIds,
                StringComparer.Ordinal).ToArray()
        })
        {
            var hostileTalent = state.SelectedTalent! with
            {
                SourceAnchorIds = anchors.Distinct(StringComparer.Ordinal).OrderBy(anchor => anchor, StringComparer.Ordinal).ToArray()
            };
            var hostileAuthority = state.Authority with
            {
                Talents = state.Authority.Talents.Select(item => item.Identity == hostileTalent.Identity ? hostileTalent : item).ToArray(),
                AuthorityDigest = string.Empty
            };
            hostileAuthority = hostileAuthority with { AuthorityDigest = CharacterCreationMagicResonanceDigest.Compute(hostileAuthority) };
            var hostile = state with
            {
                SelectedTalent = hostileTalent,
                Authority = hostileAuthority,
                Binding = state.Binding with { AuthorityDigest = hostileAuthority.AuthorityDigest },
                SnapshotDigest = string.Empty
            };
            hostile = hostile with { SnapshotDigest = CharacterCreationMagicResonanceDigest.Compute(hostile) };
            Require(CharacterCreationMagicResonanceDraftIntegrity.IsValidAuthority(hostileAuthority),
                "The hostile fixture must reach the Priority-versus-Magic source join, not fail only an outer digest.");
            Require(!CharacterCreationMagicResonanceWorkflow.TryProject(hostile, out _),
                "Missing or invented Mystic policy anchors survived the exact source join.");
        }
        var editor = projected!;
        Require(editor.MysticAdeptPowerPoints is { PowerPoints: 0, KarmaCost: 0 }
            && editor.MysticAdeptPowerPoints.MaximumPowerPoints == state.SelectedTalent!.Magic + 1,
            "Mystic Adept PP became free MAG or failed to use confirmed MAG as the purchase cap.");
        var overview = Program.NewCreationOverview(id, state.Binding.ContentRevision, state.Binding.SavedRevision) with
            { CreationMagicResonance = state, CreationMagicResonanceEditor = editor };
        var phone = new CreationMagicResonancePhoneDraft();
        phone.Bind(editor, overview);
        ExpectRejected(() => phone.CreateMysticPowerPointCandidate(-1));
        ExpectRejected(() => phone.CreateMysticPowerPointCandidate(editor.MysticAdeptPowerPoints!.MaximumPowerPoints + 1));
        var review = CharacterCreationMagicResonanceWorkflow.Review(service, editor, phone.CreateMysticPowerPointCandidate(2));
        Require(phone.TryAdopt(editor, overview, review), "Phone did not adopt the explicit PP purchase.");
        Require(review.Preview.MysticAdeptPowerPoints is { PowerPoints: 2, KarmaCost: 10 }
            && review.Preview.AdeptPowerPointBudget.Total == 2 && !review.Preview.CanConfirm,
            "Incomplete preview lost the profile-backed purchase quote or allowed missing spells/powers.");
        review = CharacterCreationMagicResonanceWorkflow.Review(service, editor,
            phone.CreateSingleCandidate(editor.Traditions.Single(item => item.Name == "Hermetic")));
        Require(phone.TryAdopt(editor, overview, review), "Tradition selection dropped the PP purchase.");
        decimal remaining = 2;
        foreach (var power in editor.AdeptPowers.Where(item => item.IsEnabled && item.PointCost > 0)
            .OrderByDescending(item => item.PointCost))
        {
            int levels = (int)Math.Min(power.MaximumLevels, decimal.Floor(remaining / power.PointCost));
            if (levels == 0) continue;
            review = CharacterCreationMagicResonanceWorkflow.Review(service, editor, phone.CreatePowerLevelCandidate(power, levels));
            Require(phone.TryAdopt(editor, overview, review), "Power selection dropped the PP purchase.");
            remaining -= levels * power.PointCost;
        }
        Require(remaining == 0, "Source power selection did not fill the reviewed PP budget.");
        foreach (var spell in editor.Spells.Where(item => item.IsEnabled).Take(state.SelectedTalent!.SpellBudget))
        {
            review = CharacterCreationMagicResonanceWorkflow.Review(service, editor, phone.CreateToggleCandidate(spell));
            Require(phone.TryAdopt(editor, overview, review), "Spell selection dropped the PP purchase.");
        }
        Require(review.Preview.CanConfirm && review.Draft.Selections.MysticAdeptPowerPoints == 2,
            string.Join(",", review.Preview.Blockers));
        var forgedPreview = review.Preview with
        {
            MysticAdeptPowerPoints = review.Preview.MysticAdeptPowerPoints! with { KarmaCost = 0 },
            PreviewDigest = string.Empty
        };
        forgedPreview = forgedPreview with { PreviewDigest = CharacterCreationMagicResonanceDigest.Compute(forgedPreview) };
        Require(!phone.TryAdopt(editor, overview, review with { Preview = forgedPreview }),
            "Native draft accepted a rehashed free-PP price despite the current source-owned quote.");
        var forgedState = state with { MysticAdeptPowerPoints = state.MysticAdeptPowerPoints! with { KarmaCost = 10 }, SnapshotDigest = string.Empty };
        forgedState = forgedState with { SnapshotDigest = CharacterCreationMagicResonanceDigest.Compute(forgedState) };
        Require(!CharacterCreationMagicResonanceWorkflow.TryProject(forgedState, out _), "Rehashed invented quote survived Presentation validation.");
        string key = CreationMagicResonancePhoneAuthority.ComputeIdempotencyKey(review);
        string beforeXml = store.Get(id).Value!.Document.Content;
        AfterRunAuthorityHarness.RunCreationMagicBackgroundAsync(contentRoot, directory, id, review.Draft)
            .GetAwaiter().GetResult();
        var confirmed = CharacterCreationMagicResonanceWorkflow.Confirm(service, review, key, explicitlyConfirmed: true);
        var coldStore = new FileWorkspaceStore(directory);
        var coldService = new CharacterCreationMagicResonanceService(coldStore, resolver);
        var cold = coldService.Load(new(id)).Value!;
        var reopened = CharacterCreationMagicResonanceWorkflow.Project(cold);
        Require(reopened.Selections.MysticAdeptPowerPoints == 2
            && reopened.MysticAdeptPowerPoints is { PowerPoints: 2, KarmaCost: 10 }
            && reopened.Budgets.Single(item => item.Kind == CharacterCreationMagicResonanceKinds.AdeptPower).Total == 2,
            "Cold phone draft lost the purchased PP or its Karma cost.");
        var replay = CharacterCreationMagicResonanceWorkflow.Confirm(coldService, review, key, explicitlyConfirmed: true);
        Require(replay.Receipt.ReceiptDigest == confirmed.Receipt.ReceiptDigest
            && coldStore.Get(id).Value!.ContentRevision == confirmed.Receipt.ContentRevision
            && coldStore.Get(id).Value!.Document.Content == beforeXml,
            "Phone purchase mutated character XML before finalization or replayed a write.");
        Console.WriteLine("PASS actual Mystic Adept profile/raised MAG → native PP purchase/powers/spells → cold file-store reopen/replay");
    }

    private static void VerifyReceiptHydration(CharacterCreationMagicResonanceReview review,
        CharacterCreationMagicResonanceConfirmation confirmation)
    {
        var backend = new MagicCheckpointBackend();
        var journal = new CharacterCreationMagicResonanceCheckpointStore(backend);
        Require(journal.TryCreate(CharacterCreationMagicResonanceCheckpoint.CreateReviewed(review),
            out var reviewed, out var blocker), blocker);
        Require(journal.TryBeginConfirm(CharacterCreationMagicResonanceCheckpointCas.From(reviewed),
            out var confirming, out blocker), blocker);
        Require(journal.TryRecordConfirmed(CharacterCreationMagicResonanceCheckpointCas.From(confirming),
            confirmation, out var stored, out blocker), blocker);
        Require(stored.IsStructurallyValid() && stored.Confirmation != confirmation,
            "Fixture must exercise the durable JSON round-trip, not the same in-memory record.");
        // Constructors only: no appearance/render or simulated Activity claim.
        _ = new CreationMagicResonanceReceiptPage(null!, stored, confirmation, journal);
        var coldJournal = new CharacterCreationMagicResonanceCheckpointStore(backend);
        Require(coldJournal.TryRead(out var reopened, out blocker), blocker);
        _ = new CreationMagicResonanceReceiptPage(null!, reopened, confirmation, coldJournal);
        foreach (var altered in new[]
        {
            confirmation with { IsCurrentDraft = false },
            confirmation with { IsIdempotentReplay = !confirmation.IsIdempotentReplay },
            confirmation with { PersistedState = confirmation.PersistedState with
                { CoreSnapshotDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8("other-snapshot") } },
            confirmation with { Receipt = confirmation.Receipt with { SavedRevision = 999 } }
        })
            ExpectRejected(() => _ = new CreationMagicResonanceReceiptPage(null!, reopened, altered, coldJournal));
        ExpectRejected(() => _ = new CreationMagicResonanceReceiptPage(null!, confirming, confirmation, coldJournal));
        Require(coldJournal.TryRead(out var unchanged, out blocker)
            && unchanged.CheckpointDigest == stored.CheckpointDigest,
            "Receipt display or rejected substitutions changed the durable journal.");
        VerifyPreferencesWorkspaceIsolation(reviewed, confirming, stored);
        Console.WriteLine("PASS receipt constructor accepts durable round-trip; changed flags, snapshot, revision and phase remain rejected");
    }

    private static void VerifyPreferencesWorkspaceIsolation(
        params CharacterCreationMagicResonanceCheckpoint[] legacyCheckpoints)
    {
        IPreferences previous = Preferences.Default;
        var setter = typeof(Preferences).GetMethod("SetDefault", System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.NonPublic)!;
        try
        {
            setter.Invoke(null, [new AfterRunAuthorityHarness.RuntimePreferences()]);
            VerifyPreferencesWorkspaceIsolationCore(legacyCheckpoints);
        }
        finally { setter.Invoke(null, [previous]); }
    }

    private static void VerifyPreferencesWorkspaceIsolationCore(
        CharacterCreationMagicResonanceCheckpoint[] legacyCheckpoints)
    {
        var first = legacyCheckpoints[0];
        string firstId = first.Review.Draft.ExpectedBinding.WorkspaceId.Value;
        string secondId = Guid.NewGuid().ToString("N");
        // Storage-only fixture: alter and rehash a real Core-issued review's
        // workspace identity. This fixture is never submitted to Core.
        var binding = first.Review.Draft.ExpectedBinding with { WorkspaceId = new(secondId) };
        var preview = first.Review.Preview with { Binding = binding, PreviewDigest = string.Empty };
        preview = preview with { PreviewDigest = CharacterCreationMagicResonanceDigest.Compute(preview) };
        var second = CharacterCreationMagicResonanceCheckpoint.CreateReviewed(first.Review with
        {
            Draft = first.Review.Draft with { ExpectedBinding = binding },
            Preview = preview
        });
        Require(second.IsStructurallyValid(), "SETUP: second runner journal is structurally invalid.");
        foreach (var owner in new[] { OwnerScope.LocalSingleUser, new OwnerScope("magic-partition-" + Guid.NewGuid().ToString("N")) })
        {
            var owners = new AfterRunAuthorityHarness.ControlledLinkedOwner();
            owners.Set(owner);
            OwnerContextStamp stamp = owners.Capture();
            bool IsCurrent(OwnerContextStamp? candidate) => candidate == owners.Capture();
            var legacyBackend = new PreferencesCharacterCreationMagicResonanceCheckpointBackend(stamp, IsCurrent);
            var secondBackend = new PreferencesCharacterCreationMagicResonanceCheckpointBackend(stamp, IsCurrent, secondId);
            CharacterCreationMagicResonanceCheckpointStore Open(string id) =>
                CharacterCreationMagicResonanceCheckpointStore.CreateDefault(stamp, IsCurrent, id);
            const string baseKey = "sr5.priority.creation.magic-resonance.checkpoint.v1";
            static string Hash(string text) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
            string ownerKey = owner == OwnerScope.LocalSingleUser ? baseKey : baseKey + ".owner." + Hash(owner.Value);
            string secondKey = ownerKey + ".workspace." + Hash(secondId);
            string previousLegacy = legacyBackend.Read();
            try
            {
                foreach (var legacy in legacyCheckpoints)
                {
                    string originalBytes = JsonSerializer.Serialize(legacy);
                    legacyBackend.Write(originalBytes);
                    var secondStore = Open(secondId);
                    Require(!secondStore.TryRead(out _, out var blocker) && string.IsNullOrEmpty(blocker),
                        "Another runner's legacy checkpoint locked this runner: " + blocker);
                    Require(secondStore.TryCreate(second, out var created, out blocker), blocker);
                    Require(!secondStore.TryBeginConfirm(CharacterCreationMagicResonanceCheckpointCas.From(legacy), out _, out _),
                        "A foreign runner's CAS advanced this journal.");
                    Require(secondStore.TryBeginConfirm(CharacterCreationMagicResonanceCheckpointCas.From(created),
                        out var pending, out blocker), blocker);
                    Require(Open(secondId).TryRead(out var cold, out blocker) && cold.CheckpointDigest == pending.CheckpointDigest,
                        "A cold scoped store lost its pending command: " + blocker);
                    Require(!secondStore.TryDeleteReviewed(CharacterCreationMagicResonanceCheckpointCas.From(pending), out _)
                        && !secondStore.TryAcknowledgeConfirmed(CharacterCreationMagicResonanceCheckpointCas.From(pending), out _),
                        "An uncertain scoped command was deletable.");
                    ExpectRejected(() => secondBackend.Write(originalBytes));
                    Require(secondStore.TryRead(out var untouched, out blocker) && untouched.CheckpointDigest == pending.CheckpointDigest,
                        "A rejected cross-runner write changed this journal.");
                    Require(legacyBackend.Read() == originalBytes
                        && Open(firstId).TryRead(out var old, out blocker) && old.CheckpointDigest == legacy.CheckpointDigest,
                        "The original runner lost or modified its legacy recovery.");
                    if (legacy.Phase == CharacterCreationMagicResonanceCheckpointPhase.Confirmed)
                    {
                        Require(Open(firstId).TryAcknowledgeConfirmed(CharacterCreationMagicResonanceCheckpointCas.From(legacy), out blocker), blocker);
                        Require(string.IsNullOrEmpty(legacyBackend.Read()) && Open(secondId).TryRead(out _, out _),
                            "Acknowledging the legacy runner removed the new runner's command.");
                    }
                    // Fixture teardown only; never used by production recovery.
                    Preferences.Default.Remove(secondKey);
                }
                legacyBackend.Write("{broken");
                Require(!Open(secondId).TryRead(out _, out var malformed) && !string.IsNullOrEmpty(malformed)
                    && !Open(secondId).TryCreate(second, out _, out _)
                    && legacyBackend.Read() == "{broken", "Malformed legacy data was ignored or overwritten.");
                legacyBackend.Remove();
                var scoped = Open(secondId);
                Require(scoped.TryCreate(second, out _, out var error), error);
                string secondBytes = secondBackend.Read();
                legacyBackend.Write(secondBytes);
                Require(!scoped.TryRead(out _, out var conflict) && !string.IsNullOrEmpty(conflict)
                    && !scoped.TryDeleteReviewed(CharacterCreationMagicResonanceCheckpointCas.From(second), out _)
                    && Preferences.Default.Get(secondKey, "") == secondBytes && legacyBackend.Read() == secondBytes,
                    "Conflicting same-runner journals were admitted or deleted.");
                legacyBackend.Remove();
                foreach (string bad in new[] { "{broken", JsonSerializer.Serialize(first) })
                {
                    Preferences.Default.Set(secondKey, bad);
                    Require(!scoped.TryRead(out _, out var invalid) && !string.IsNullOrEmpty(invalid)
                        && !scoped.TryCreate(second, out _, out _) && Preferences.Default.Get(secondKey, "") == bad,
                        "A malformed or wrong-runner scoped journal was accepted or overwritten.");
                }
                Preferences.Default.Set(secondKey, secondBytes);
                owners.Set(new OwnerScope("other-magic-" + Guid.NewGuid().ToString("N")));
                Require(!CharacterCreationMagicResonanceCheckpointStore.CreateDefault(owners.Capture(), IsCurrent, secondId)
                    .TryRead(out _, out var other) && string.IsNullOrEmpty(other), "Another owner read the runner's journal.");
                owners.Set(owner);
                Require(!scoped.TryRead(out _, out var stale) && !string.IsNullOrEmpty(stale)
                    && !scoped.TryDeleteReviewed(CharacterCreationMagicResonanceCheckpointCas.From(second), out _),
                    "An old owner epoch retained scoped checkpoint access.");
                var fresh = CharacterCreationMagicResonanceCheckpointStore.CreateDefault(owners.Capture(), IsCurrent, secondId);
                Require(fresh.TryRead(out var restored, out _) && restored.CheckpointDigest == second.CheckpointDigest,
                    "Fresh same-owner authority could not recover the scoped checkpoint.");
            }
            finally
            {
                Preferences.Default.Remove(secondKey);
                if (string.IsNullOrEmpty(previousLegacy)) Preferences.Default.Remove(ownerKey);
                else Preferences.Default.Set(ownerKey, previousLegacy);
            }
        }
        Console.WriteLine("PASS Preferences runner partitions: all legacy phases retained, cold pending recovery, CAS/owner ABA, malformed/conflict rejection");
    }

    private sealed class MagicCheckpointBackend : ICharacterCreationMagicResonanceCheckpointBackend
    {
        private string _payload = string.Empty;
        public string Read() => _payload;
        public void Write(string payload) => _payload = payload;
        public void Remove() => _payload = string.Empty;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static string ProjectionDiagnostics(CharacterCreationMagicResonanceState state)
    {
        bool Probe(string name, params object[] args) => (bool)typeof(CharacterCreationMagicResonanceWorkflow)
            .GetMethod(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, args)!;
        return $"binding={Probe("BindingDigestsAreCanonical", state.Binding)}; "
            + $"priority={Probe("PrerequisiteSelectsExactTalent", state.PrerequisiteDraft!, state.SelectedTalent!, state.Authority.MysticAdeptPowerPointPolicy!)}; "
            + $"budgets={Probe("BudgetsAreValid", state)}; "
            + $"authority={CharacterCreationMagicResonanceDraftIntegrity.IsValidAuthority(state.Authority)}; "
            + $"revision={state.Binding.ContentRevision}/{state.Binding.SavedRevision}; "
            + $"attributes={CharacterCreationMagicResonanceFinalizationRules.TryResolveEffectiveAttributes(state.SelectedTalent!, state.AttributesDraft!, out _)}; "
            + $"profile={state.PrerequisiteDraft!.SettingsProfileId == state.Authority.SettingsProfileId}; "
            + $"prerequisite-authority={state.Binding.PrerequisiteAuthorityDigest == state.Authority.PrerequisiteAuthorityDigest}; "
            + $"snapshot={state.SnapshotDigest == CharacterCreationMagicResonanceDigest.Compute(state with { SnapshotDigest = string.Empty })}";
    }
    private static void ExpectRejected(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("A stale or out-of-range phone selection was accepted.");
    }
}
