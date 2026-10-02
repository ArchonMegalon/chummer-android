using System.Reflection;
using System.Globalization;
using System.Text.RegularExpressions;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation.Overview;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    private static void VerifyQualitySummaryContent(string contentRoot)
    {
        var catalog = System.Xml.Linq.XDocument.Load(Path.Combine(contentRoot, "data", "qualities.xml"))
            .Root!.Element("qualities")!.Elements("quality").ToArray();
        static string SummaryKey(System.Xml.Linq.XElement quality)
            => "Qualities.Summary." + Guid.Parse(quality.Element("id")!.Value).ToString("D");
        var oldCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (string locale in new[] { "en-GB", "de-AT", "es-MX" })
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
                int authored = 0, missing = 0, partial = 0;
                foreach (var quality in catalog)
                {
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    if (summary.Length > 0) authored++;
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(lines.Count > 0 && lines.All(line => !string.IsNullOrWhiteSpace(line)),
                        "Every catalog entry needs an explanation or an honest missing-description state.");
                    string text = string.Join(" ", lines);
                    Require(!Regex.IsMatch(text, @"[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}", RegexOptions.IgnoreCase)
                        && !text.Contains("rulebook", StringComparison.OrdinalIgnoreCase)
                        && !text.Contains("Regelbuch", StringComparison.OrdinalIgnoreCase)
                        && !text.Contains("consulta el manual", StringComparison.OrdinalIgnoreCase),
                        "Quality information exposed a machine identity or deferred to a book.");
                    if (lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", ""))) missing++;
                    if (lines.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", ""))) partial++;
                    if (summary.Length > 0) Require(lines[0] == summary,
                        "The original summary must precede technical effects: " + quality.Element("name")!.Value);
                }
                Require(authored >= 332, "Localized source-identity summaries were not loaded from the real catalog.");
                foreach (string name in new[] { "Hung Out to Dry", "Night Blindness", "Paranoia",
                    "Vendetta", "Pie Iesu Domine. Dona Eis Requiem.",
                    "Carrier (HMHVV Strain II)", "Carrier (HMHVV Strain III)" })
                {
                    var quality = catalog.Single(q => q.Element("name")!.Value == name);
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(summary.Length > 40 && lines[0] == summary
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", "")),
                        "Conditional drawbacks need translated explanations bound to the consumed definition: " + name);
                }
                var carrierSummaries = catalog.Where(q => q.Element("name")!.Value.StartsWith("Carrier (HMHVV", StringComparison.Ordinal))
                    .Select(q => CreationFlowStrings.Get(SummaryKey(q), "")).ToArray();
                Require(carrierSummaries.Length == 2 && carrierSummaries.Distinct().Count() == 2
                    && carrierSummaries.All(text => Regex.Matches(text, @"\d+").Select(m => m.Value).SequenceEqual(new[] { "2", "1" })),
                    "Carrier strains must remain distinct without losing their translated dice and reputation values.");
                foreach (string name in new[] { "Hawk Eye", "Jack of All Trades Master of None",
                    "Lightning Reflexes", "Linguist", "Sensei", "Trustworthy", "Witness My Hate",
                    "Illiterate", "Deaf" })
                {
                    var quality = catalog.Single(quality => quality.Element("name")!.Value == name);
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(summary.Length > 40 && lines[0] == summary
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", "")),
                        "Talents, training and sensory drawbacks need translated, definition-bound explanations: " + name);
                }
                var inspiredVariants = catalog.Where(q => q.Element("name")!.Value == "Inspired").ToArray();
                Require(inspiredVariants.Length == 2, "The consumed catalog has two distinct Inspired definitions.");
                foreach (var quality in inspiredVariants)
                {
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(summary.Length > 40 && lines[0] == summary,
                        "Same-name qualities must retain their own translated, exact-definition summary.");
                }
                Require(inspiredVariants.Select(q => CreationFlowStrings.Get(SummaryKey(q), "")).Distinct().Count() == 2,
                    "Inspired's skill bonus must not overwrite its different expertise variant.");
                Require(CreationQualityInfo.Effects(catalog.Single(q => q.Element("name")!.Value == "Sensei").ToString())
                    .Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                    "Sensei's summary must not hide unresolved contact/skill selection details.");
                foreach (string name in new[] { "Phobia (Uncommon, Mild)", "Phobia (Uncommon, Moderate)",
                    "Phobia (Uncommon, Severe)", "Phobia (Common, Mild)", "Phobia (Common, Moderate)",
                    "Phobia (Common, Severe)", "Poor Self Control (Braggart)",
                    "Poor Self Control (Thrill Seeker)", "Poor Self Control (Vindictive)",
                    "Poor Self Control (Combat Monster)" })
                {
                    var quality = catalog.Single(quality => quality.Element("name")!.Value == name);
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(summary.Length > 40 && lines[0] == summary
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", "")),
                        "Fear and impulse variants need translated, definition-bound consequences: " + name);
                }
                foreach (string frequency in new[] { "Common", "Uncommon" })
                {
                    foreach (var grade in new[] { (Name: "Mild", Numbers: "1"),
                        (Name: "Moderate", Numbers: "3,2"), (Name: "Severe", Numbers: "6,5,5") })
                    {
                        var quality = catalog.Single(q => q.Element("name")!.Value == $"Phobia ({frequency}, {grade.Name})");
                        string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                        string numbers = string.Join(",", Regex.Matches(summary, @"\d+").Select(match => match.Value));
                        Require(numbers == grade.Numbers,
                            "Frequency must not change severity or lose the translated fear penalty, threshold or duration.");
                    }
                }
                foreach (string name in new[] { "Albinism I", "Albinism II",
                    "Amnesia (Surface Loss)", "Amnesia (Neural Deletion)",
                    "Day Job (10 hrs)", "Day Job (20 hrs)", "Day Job (40 hrs)", "In Debt",
                    "Incomplete Deprogramming", "Oblivious I", "Oblivious II",
                    "Pacifist I", "Pacifist II", "Records on File", "Sensory Overload Syndrome", "Wanted" })
                {
                    var quality = catalog.Single(quality => quality.Element("name")!.Value == name);
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(summary.Length > 40 && lines[0] == summary
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", "")),
                        "Drawback variants and obligations need translated, definition-bound explanations: " + name);
                }
                foreach (string name in new[] { "Asthma", "Big Regret", "Blind", "Borrowed Time",
                    "Computer Illiterate", "Creature of Comfort (Middle)", "Creature of Comfort (High)",
                    "Creature of Comfort (Luxury)", "Did You Just Call Me Dumb?", "Driven",
                    "Emotional Attachment", "Ex-Con", "Flashbacks I", "Flashbacks II",
                    "Hobo with a Shotgun", "Paraplegic", "Signature" })
                {
                    var quality = catalog.Single(quality => quality.Element("name")!.Value == name);
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(summary.Length > 40 && lines[0] == summary
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", "")),
                        "Run Faster drawbacks need translated, definition-bound consequences beyond names and selection prompts: " + name);
                }
                foreach (string name in new[] { "Adrenaline Surge", "Common Sense", "Daredevil",
                    "Digital Doppelganger", "Disgraced", "Night Vision", "Perfect Time", "Poor Link",
                    "Privileged Family Name", "Solid Rep", "Legendary Rep", "Speed Reading",
                    "Spike Resistance", "Spirit Whisperer", "Steely Eyed Wheelman" })
                {
                    var quality = catalog.Single(quality => quality.Element("name")!.Value == name);
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(summary.Length > 40 && lines[0] == summary
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", "")),
                        "Run Faster benefits need translated, definition-bound explanations, not empty bonus nodes or prompts: " + name);
                }
                foreach (var quality in catalog.Where(quality => quality.Element("source")?.Value == "SR5"))
                {
                    string name = quality.Element("name")!.Value;
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(summary.Length > 40 && lines[0] == summary
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", "")),
                        "Every SR5 core-source entry needs an original, translated summary, including hidden granted traits: " + name);
                }
                foreach (string name in new[] { "Codeslinger", "Spirit Affinity",
                    "Infected Advanced Optional Power: Mimicry", "Infected Advanced Optional Power: Psychokinesis" })
                {
                    var quality = catalog.Single(quality => quality.Element("name")!.Value == name);
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(lines.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                        "A helpful summary must not hide unresolved action, spirit-picker or external-power details: " + name);
                }
                foreach (var quality in catalog.Where(quality => quality.Element("name")!.Value.StartsWith("SINner (", StringComparison.Ordinal)
                    || quality.Element("name")!.Value.StartsWith("Prejudiced (", StringComparison.Ordinal)))
                {
                    string name = quality.Element("name")!.Value;
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    bool unresolvedIssuerChoice = name is "SINner (Corporate)" or "SINner (Corporate Limited)";
                    Require(summary.Length > 40 && lines[0] == summary
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", ""))
                        && lines.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")) == unresolvedIssuerChoice,
                        "SIN/prejudice help must explain each variant without concealing unresolved corporate issuer-picker details: " + name);
                    if (name.StartsWith("SINner (", StringComparison.Ordinal))
                    {
                        int tax = name == "SINner (Corporate)" ? 10 : name == "SINner (Corporate Limited)" ? 20 : 15;
                        Require(Regex.IsMatch(summary, $@"\b{tax}\s?%")
                            && Regex.Matches(summary, @"\b\d+\s?%").Count == 1,
                            "Every translated SIN variant must retain its own gross-income tax rate: " + name);
                    }
                    else
                    {
                        int dice = name.EndsWith("Biased)", StringComparison.Ordinal) ? 2
                            : name.EndsWith("Outspoken)", StringComparison.Ordinal) ? 4 : 6;
                        Require(summary.Contains($"{dice}", StringComparison.Ordinal)
                            && summary.Contains($"+{dice}", StringComparison.Ordinal),
                            "Translated prejudice help must retain both the player's penalty and the target's negotiation bonus: " + name);
                    }
                }
                foreach (var quality in catalog.Where(quality => quality.Element("name")!.Value.StartsWith("Allergy (", StringComparison.Ordinal)
                    || quality.Element("name")!.Value.StartsWith("Addiction (", StringComparison.Ordinal)))
                {
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(summary.Length > 40 && lines[0] == summary
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", ""))
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                        "Every allergy and addiction grade needs translated, definition-bound effects beyond its choice prompt or reputation.");
                }
                foreach (string name in new[] { "Astral Beacon", "Bad Luck", "Combat Paralysis", "Codeblock",
                    "Distinctive Style", "Insomnia (Basic)", "Insomnia (Full)", "Simsense Vertigo",
                    "Low Pain Tolerance", "Elf Poser", "Ork Poser", "Spirit Bane" })
                {
                    var quality = catalog.Single(quality => quality.Element("name")!.Value == name);
                    string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(summary.Length > 40 && lines[0] == summary
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", ""))
                        && lines.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")) == (name == "Spirit Bane"),
                        "Negative qualities need their own translated rules, not just reputation or a choice prompt: " + name);
                    // Spirit Bane's type picker references another catalog via
                    // selecttext attributes. Do not mark that unresolved detail
                    // complete just because its common rules now have prose.
                }
                var changedQuality = new System.Xml.Linq.XElement(catalog.Single(quality => quality.Element("name")!.Value == "Will to Live"));
                string originalSummary = CreationFlowStrings.Get("Qualities.Summary." + changedQuality.Element("id")!.Value, "");
                changedQuality.Element("bonus")!.Element("conditionmonitor")!.Element("overflow")!.Value = "4";
                var changedHelp = CreationQualityInfo.Effects(changedQuality.ToString());
                Require(!changedHelp.Contains(originalSummary),
                    "A house-rule definition retaining an official ID must not display the original one-box explanation.");
                Require(changedHelp[0] == CreationFlowStrings.Get("Qualities.Info.ChangedDefinition", "")
                    && changedHelp.Any(line => line.Contains(": 4", StringComparison.Ordinal)),
                    "Changed definitions must explain the mismatch and retain their actual encoded values in every locale.");
                foreach (var original in catalog.Where(quality => CreationFlowStrings.Get(SummaryKey(quality), "").Length > 0))
                {
                    string prose = CreationFlowStrings.Get(SummaryKey(original), "");
                    Require(CreationQualityInfo.Effects(original.ToString(System.Xml.Linq.SaveOptions.DisableFormatting))[0] == prose,
                        "Formatting alone must not discard a reviewed explanation.");
                    foreach (string field in new[] { "karma", "limit", "required", "bonus" })
                    {
                        var amended = new System.Xml.Linq.XElement(original);
                        amended.SetElementValue(field, "changed-definition");
                        var lines = CreationQualityInfo.Effects(amended.ToString());
                        Require(!lines.Contains(prose) && lines[0] == CreationFlowStrings.Get("Qualities.Info.ChangedDefinition", ""),
                            "Same-ID changes to cost, limits, prerequisites or effects must not borrow reviewed prose: " + field);
                    }
                }
                foreach (string name in new[] { "Prototype Transhuman", "Wildcard Chimera",
                    "Resonant Stream: Technoshaman", "Resonant Stream: Cyberadept" })
                {
                    var quality = catalog.Single(quality => quality.Element("name")!.Value == name);
                    string summary = CreationFlowStrings.Get("Qualities.Summary." + quality.Element("id")!.Value, "");
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(summary.Length > 80 && lines[0] == summary
                        && !lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", ""))
                        && lines.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                        "Special choices need translated, useful help without concealing unexpanded rules: " + name);
                }
                string willToLive = catalog.Single(quality => quality.Element("name")!.Value == "Will to Live").ToString();
                var rated = CreationQualityInfo.Effects(willToLive, 3);
                string levelNotice = CreationFlowStrings.Format("Qualities.Info.BaseEffects", "missing", 3);
                Require(rated[1] == levelNotice && levelNotice.Contains("3")
                    && rated[2].Contains("1") && !rated[2].Contains("3")
                    && !CreationQualityInfo.Effects(willToLive, 1).Contains(levelNotice),
                    "A rated quality must distinguish its source values from the selected level, without Android multiplying them.");
                foreach (var quality in catalog.Where(quality => quality.Element("naturalweapons") is not null))
                {
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    foreach (var weapon in quality.Element("naturalweapons")!.Elements("naturalweapon"))
                    {
                        string weaponName = weapon.Element("name")!.Value;
                        string translated = CreationFlowStrings.Get("Qualities.Value." + weaponName, weaponName);
                        Require(lines.Any(line => line.StartsWith(CreationFlowStrings.Get("Qualities.Effect.Natural weapon", "") + ": " + translated,
                                StringComparison.Ordinal)), "Every encoded natural weapon must have a readable help entry in every locale.");
                    }
                }
                string fractionalEssence = string.Join(" ", CreationQualityInfo.Effects(
                    "<quality><bonus><essencepenaltyt100>-150</essencepenaltyt100></bonus></quality>"));
                Require(fractionalEssence.Contains((-1.5m).ToString(CultureInfo.CurrentUICulture))
                    && !fractionalEssence.Contains("-150"), "Hundredths must use readable, localized display units.");
                Console.WriteLine($"QUALITY_COPY locale={locale} catalog={catalog.Length} authored={authored} missing={missing} partial={partial}");
            }
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-GB");
            string Effect(string name) => string.Join(" ", CreationQualityInfo.Effects(
                catalog.Single(quality => quality.Element("name")!.Value == name).ToString()));
            string EffectById(string id) => string.Join(" ", CreationQualityInfo.Effects(
                catalog.Single(quality => quality.Element("id")!.Value == id).ToString()));
            Require(Effect("Night Blindness").Contains("one category worse")
                && Effect("Night Blindness").Contains("full light without glare remains penalty-free")
                && Effect("Night Blindness").Contains("buying off this drawback first"),
                "Night blindness needs its lighting exception and correction restriction.");
            Require(Effect("Paranoia").Contains("Loyalty is below 4")
                && Effect("Paranoia").Contains("relocate every few months")
                && Effect("Vendetta").Contains("Composure (3)")
                && Effect("Vendetta").Contains("buy it off, or a new enemy"),
                "Conditional social and feud drawbacks must retain thresholds, obligations and exit conditions.");
            Require(Effect("Hung Out to Dry").Contains("equal value")
                && Effect("Pie Iesu Domine. Dona Eis Requiem.").Contains("High Pain Tolerance 1")
                && Effect("Pie Iesu Domine. Dona Eis Requiem.").Contains("1 Physical damage box")
                && Effect("Pie Iesu Domine. Dona Eis Requiem.").Contains("compulsion"),
                "Removing a social drawback has a cost, and a granted benefit must not hide its recurring cost.");
            foreach (string strain in new[] { "II", "III" })
                Require(Effect($"Carrier (HMHVV Strain {strain})").StartsWith($"HMHVV-{strain} carrier:", StringComparison.Ordinal)
                    && Effect($"Carrier (HMHVV Strain {strain})").Contains("requires disease resistance")
                    && Effect($"Carrier (HMHVV Strain {strain})").Contains("Immune only")
                    && Effect($"Carrier (HMHVV Strain {strain})").Contains("aware of your status"),
                    "Carrier status is neither automatic infection, universal immunity nor a blanket social penalty.");
            Require(Effect("Hawk Eye").Contains("range penalties as one category nearer")
                && Effect("Hawk Eye").Contains("Incompatible with electronic vision enhancements")
                && Effect("Lightning Reflexes").Contains("+1 Initiative rating, +1 Initiative die")
                && Effect("Lightning Reflexes").Contains("do not stack with technological, chemical or magical enhancements"),
                "Natural vision and reflexes must retain their augmentation restrictions and distinct initiative values.");
            string inspiredTalent = EffectById("f8f216b5-1c29-467d-9fb5-c9812408203d");
            string inspiredExpertise = EffectById("fd9b9b6d-c969-40f1-8dc7-61f8e5d9cd4d");
            Require(inspiredTalent.Contains("Choose Artisan or Performance")
                && inspiredTalent.Contains("only among artists who know your reputation")
                && inspiredTalent.Contains("does not grant a specialization")
                && inspiredExpertise.StartsWith("Choose a free expertise specialization in Artisan.", StringComparison.Ordinal)
                && !inspiredExpertise.Contains("Street Cred"),
                "The two Inspired definitions share a name but not their skill choice, reputation or expertise benefits.");
            Require(Effect("Jack of All Trades Master of None").Contains("After creation")
                && Effect("Jack of All Trades Master of None").Contains("rating 5 or lower costs 1 less Karma, minimum 1")
                && Effect("Jack of All Trades Master of None").Contains("rating 6 or higher cost 2 extra Karma")
                && Effect("Linguist").Contains("half as long")
                && Effect("Linguist").Contains("At creation, language points buy twice as much")
                && Effect("Linguist").Contains("rating 3 or higher costs 1 less Karma"),
                "Training discounts must keep creation/Career boundaries and the higher-rating surcharge.");
            Require(Effect("Sensei").Contains("Connection 3+")
                && Effect("Sensei").Contains("teacher rating 13; Instruction 10 dice, limit 7")
                && Effect("Sensei").Contains("rating 12; Instruction 12 dice, limit 8")
                && Effect("Sensei").Contains("not free ranks for you")
                && Effect("Trustworthy").Contains("only when the situation involves trusting you"),
                "A teacher's skill ratings are not the runner's, and a trust-based limit is not a blanket social modifier.");
            Require(Effect("Witness My Hate").Contains("Single-target direct combat spells")
                && Effect("Witness My Hate").Contains("2 more damage but cause 2 more Drain")
                && Effect("Witness My Hate").Contains("does not improve indirect or area spells")
                && Effect("Illiterate").Contains("others' electronics, not your own")
                && Effect("Illiterate").Contains("cost double Karma afterward until you learn to read and buy off")
                && Effect("Deaf").Contains("audio-only Perception automatically fails")
                && Effect("Deaf").Contains("General Perception loses 2 dice; Surprise loses 3"),
                "Spell damage needs its Drain tradeoff; sensory drawbacks must retain their affected tests and recovery requirements.");
            Require(Effect("Codeslinger").Contains("one Matrix action that requires a test")
                && Effect("Codeslinger").Contains("two dice")
                && Effect("Home Ground").Contains("Only the selected benefit applies")
                && Effect("Home Ground").Contains("+2 Street Cred"),
                "Conditional action and home-ground benefits must not turn into blanket bonuses or bonus Karma.");
            foreach (string name in new[] { "Natural Immunity (Natural)", "Natural Immunity (Synthetic)" })
                Require(Effect(name).Contains("One exposure per 6 hours")
                    && Effect(name).Contains("further exposures cause normal damage with half-time recovery")
                    && Effect(name).Contains("Excludes magical agents"),
                    "Selected-agent immunity must retain its interval, repeated-exposure damage and magical-agent exclusion.");
            Require(Effect("Magician").Contains("does not grant every skill, spell or adept Power Points")
                && Effect("Aspected Magician").Contains("Choose exactly one group")
                && Effect("Aspected Magician").Contains("cannot project")
                && Effect("Astral Perception").Contains("mundane physical tasks lose two dice")
                && Effect("Astral Perception").Contains("not a free adept-power purchase"),
                "Magic aptitude, an aspected skill group and a granted astral sense have different capabilities and costs.");
            Require(Effect("Low-Light Vision").Contains("total darkness still blocks")
                && Effect("Thermographic Vision").Contains("by one step")
                && Effect("Thermographic Vision").Contains("not a flat Perception bonus"),
                "Low-light and heat vision must retain distinct environmental limits.");
            Require(Effect("Spirit Affinity").Contains("one extra service")
                && Effect("Spirit Affinity").Contains("one die on Binding")
                && Effect("Infected Advanced Optional Power: Mimicry").Contains("imitates sound, not the speaker's appearance")
                && Effect("Infected Advanced Optional Power: Psychokinesis").Contains("hand's Strength and Agility, not your own attributes"),
                "Spirit and infected-power help must distinguish services, imitation and a telekinetic hand from personal attribute bonuses.");
            Require(Effect("Code of Honor").Contains("each protected death costs 1 adventure Karma")
                && Effect("Scorched").Contains("Body + Willpower (4)")
                && Effect("Scorched").Contains("6 hours, a glitch for 24")
                && Effect("Scorched").Contains("−2 dice to resist its damage"),
                "Moral restrictions and neurological aftereffects need actual consequences beyond their selection prompt or reputation.");
            Require(Effect("Catlike").Contains("Sneaking") && Effect("Catlike").Contains("Bonus: 2"),
                "Specific-skill modifiers must be shown, not silently replaced by generic copy.");
            Require(Effect("Adrenaline Surge").Contains("first Initiative Pass of a new combat")
                && Effect("Adrenaline Surge").Contains("Being surprised still prevents")
                && Effect("Common Sense").Contains("Edge rating in warnings per session")
                && Effect("Daredevil").Contains("recover 2 points instead of 1"),
                "Initiative priority must not remove surprise, warnings need their session cap, and recovered Edge is not maximum Edge.");
            Require(Effect("Digital Doppelganger").Contains("fake SIN rated at least 4")
                && Effect("Digital Doppelganger").Contains("raising the threshold")
                && Effect("Digital Doppelganger").Contains("Other identities are not protected")
                && Effect("Disgraced").Contains("2 dice to Intimidation against criminals")
                && Effect("Disgraced").Contains("prejudiced attitude toward you"),
                "Identity-scoped searches and intimidation benefits must retain their targets and social downside.");
            Require(Effect("Night Vision").Contains("moderate glare on overcast days")
                && Effect("Night Vision").Contains("without a Karma refund")
                && Effect("Perfect Time").Contains("Free Action each Action Phase")
                && Effect("Perfect Time").Contains("not a Simple or Complex Action"),
                "Night Vision needs its glare/loss drawbacks, and Perfect Time must not grant a full attack action.");
            Require(Effect("Poor Link").Contains("Both effects apply to friendly rituals too")
                && Effect("Privileged Family Name").Contains("Minor local NPCs lose 2 dice")
                && Effect("Privileged Family Name").Contains("national or full corporate SIN")
                && Effect("Solid Rep").Contains("improves by 1")
                && Effect("Legendary Rep").Contains("improves by 2"),
                "Ritual resistance and local reputation benefits must preserve their directions, identity requirements and distinct values.");
            Require(Effect("Speed Reading").Contains("800 words in five seconds")
                && Effect("Speed Reading").Contains("does not automatically memorize")
                && Effect("Spike Resistance").Contains("Each level adds 1 die")
                && Effect("Spike Resistance").Contains("up to 3 levels"),
                "Reading speed must not become perfect recall, and biofeedback resistance is per level, not extra Matrix armor.");
            Require(Effect("Spirit Whisperer").Contains("Spirits gain 1 extra die to resist your Summoning")
                && Effect("Spirit Whisperer").Contains("the summoning itself still uses the declared Force")
                && Effect("Steely Eyed Wheelman").Contains("by 1, never below 0"),
                "The spirit's resistance bonus must not be given to its summoner, and reduced terrain penalties cannot become a bonus.");
            Require(Effect("Albinism I").Contains("Cybereye-compatible")
                && Effect("Albinism II").Contains("before other Karma spending")
                && Effect("Amnesia (Surface Loss)").Contains("2 Karma and adds one rank")
                && Effect("Amnesia (Neural Deletion)").Contains("GM-led play")
                && Effect("Amnesia (Neural Deletion)").Contains("Karma buyoff and the GM's story goals"),
                "Variant help must distinguish reduced symptoms and GM-mediated memory recovery without promising automatic app behavior.");
            foreach (var job in new[] { (Hours: 10, Pay: "1,000"), (Hours: 20, Pay: "2,500"), (Hours: 40, Pay: "5,000") })
                Require(Effect($"Day Job ({job.Hours} hrs)").Contains($"{job.Hours} hours/week, ¥{job.Pay}/month")
                    && Effect($"Day Job ({job.Hours} hrs)").Contains("fake 4+"),
                    "Each schedule must retain its own working hours, monthly salary and identity requirement.");
            Require(Effect("In Debt").Contains("150%") && Effect("In Debt").Contains("10% monthly")
                && Effect("In Debt").Contains("unhealable until paid")
                && Effect("In Debt").Contains("Repayment is required in both cases")
                && Effect("Incomplete Deprogramming").Contains("Composure (4)")
                && Effect("Incomplete Deprogramming").Contains("1D6 minutes")
                && Effect("Incomplete Deprogramming").Contains("skills become unavailable"),
                "Debts need ongoing obligations, and identity switches must not be described as permanent skill loss.");
            Require(new[] { "Oblivious I", "Oblivious II" }.All(name => Effect(name).Contains("astral and Matrix"))
                && Effect("Oblivious I").Contains("does not raise")
                && Effect("Oblivious II").Contains("thresholds by 1")
                && Effect("Pacifist I").Contains("ongoing attack")
                && Effect("Pacifist II").Contains("(20, daily)")
                && Effect("Pacifist II").Contains("weekly recovery"),
                "Higher grades must preserve their distinct thresholds and recovery intervals rather than copying the lower-grade effect.");
            Require(Effect("Records on File").Contains("Its agents gain 2 dice")
                && Effect("Records on File").Contains("security zones C or better")
                && Effect("Records on File").Contains(CreationFlowStrings.Get("Qualities.Info.Additional", ""))
                && Effect("Sensory Overload Syndrome").Contains("Willpower + Edge (4)")
                && Effect("Sensory Overload Syndrome").Contains("5 − hits minutes")
                && Effect("Wanted").Contains("¥25,000")
                && Effect("Wanted").Contains("buy it off with Karma"),
                "Investigators' advantages, unresolved corporation choices, timed overload and continuing bounty obligations must remain explicit.");
            foreach (string frequency in new[] { "Common", "Uncommon" })
            {
                string mild = Effect($"Phobia ({frequency}, Mild)");
                string moderate = Effect($"Phobia ({frequency}, Moderate)");
                string severe = Effect($"Phobia ({frequency}, Severe)");
                Require(mild.Contains("While exposed") && mild.Contains("all actions lose 1 die")
                    && !mild.Contains("Composure (") && moderate.Contains("while exposed")
                    && moderate.Contains("all actions lose 3 dice") && moderate.Contains("Composure (2)")
                    && severe.Contains("while exposed") && severe.Contains("all actions lose 6 dice")
                    && severe.Contains("Composure (5)") && severe.Contains("at least 5 − hits Combat Turns"),
                    "Fear penalties apply only in the trigger's presence; each grade has distinct resistance and flight behavior.");
                Require(mild.Contains(frequency == "Common" ? "frequent trigger" : "rare trigger"),
                    "Phobia frequency must remain distinct from severity.");
            }
            Require(Effect("Poor Self Control (Braggart)").Contains("Composure (3)")
                && Effect("Poor Self Control (Thrill Seeker)").Contains("Composure (2)")
                && Effect("Poor Self Control (Thrill Seeker)").Contains("+1 Initiative Score for 5 Combat Turns")
                && Effect("Poor Self Control (Thrill Seeker)").Contains("not an extra die or +1 each turn")
                && Effect("Poor Self Control (Vindictive)").Contains("Composure (2)")
                && Effect("Poor Self Control (Vindictive)").Contains("still plan to settle the score later")
                && Effect("Poor Self Control (Combat Monster)").Contains("Composure (3)")
                && Effect("Poor Self Control (Combat Monster)").Contains("every opponent is incapacitated"),
                "Impulse help must distinguish thresholds, retained grudges, withdrawal and a temporary initiative-score bonus.");
            Require(Effect("Asthma").Contains("twice as often")
                && Effect("Asthma").Contains("Effects accumulate")
                && Effect("Asthma").Contains("at 4, resist further Fatigue using only Willpower")
                && Effect("Asthma").Contains("at 8, another −1"),
                "Asthma needs cumulative thresholds, not a permanent generic penalty or doubled damage amount.");
            Require(Effect("Big Regret").Contains("Social Limit drops by 3")
                && Effect("Big Regret").Contains("cannot buy this quality off while it stays secret")
                && Effect("Blind").Contains("general Perception loses 4 dice and Surprise loses 3")
                && Effect("Blind").Contains("Cybereyes cannot fix")
                && Effect("Blind").Contains("−2 dice for physical-plane actions"),
                "A secret's conditional social limit and blindness's distinct perception/astral consequences must remain clear.");
            Require(Effect("Borrowed Time").Contains("any triple")
                && Effect("Borrowed Time").Contains("permanently burning all current Edge")
                && Effect("Computer Illiterate").Contains("electronic devices or Matrix-connected systems")
                && Effect("Computer Illiterate").Contains("does not take the same penalty twice"),
                "Unavoidable mortality must not become ordinary Edge spending, and electronic penalties must not stack twice.");
            foreach (string tier in new[] { "Middle", "High", "Luxury" })
                Require(Effect($"Creature of Comfort ({tier})").Contains($"Below {tier} Lifestyle")
                    && Effect($"Creature of Comfort ({tier})").Contains($"per tier below {tier}"),
                    "Each comfort variant needs its own lifestyle baseline, not an accumulating per-day modifier.");
            Require(Effect("Did You Just Call Me Dumb?").Contains("critical glitch, even if the roll also has hits")
                && Effect("Driven").Contains("Willpower + Logic (4)")
                && Effect("Driven").Contains("While actively following a lead, Willpower increases by 1")
                && Effect("Emotional Attachment").Contains("six months")
                && Effect("Emotional Attachment").Contains("attachment transfers to replacement gear"),
                "Social glitches, conditional obsession benefits and lasting equipment loss need their actual consequences.");
            Require(Effect("Ex-Con").Contains("two Matrix check-ins and one personal visit weekly")
                && Effect("Ex-Con").Contains("Corporate contacts need Loyalty 4+, police contacts 5+")
                && Effect("Flashbacks I").Contains("about every other run")
                && Effect("Flashbacks II").Contains("at least once each session")
                && new[] { "Flashbacks I", "Flashbacks II" }.All(name =>
                    Effect(name).Contains("Composure (5)") && Effect(name).Contains("5 − hits Combat Turns")),
                "Parole requires real obligations, and flashback grades change frequency rather than the same resistance test.");
            Require(Effect("Hobo with a Shotgun").Contains("every Mental attribute by 2")
                && Effect("Hobo with a Shotgun").Contains("full day at Squatter or Street")
                && Effect("Paraplegic").Contains("10%; vehicles need 5% modifications or a rigger interface")
                && Effect("Paraplegic").Contains("Astral and Matrix abilities are unaffected")
                && Effect("Signature").Contains("Investigators")
                && Effect("Signature").Contains("Street Cred plus Public Awareness"),
                "Lifestyle discomfort, mobility costs and identification modifiers must retain their affected actors and recovery conditions.");
            Require(Effect("Exceptional Attribute").Contains("Maximum change: 1")
                && Effect("Exceptional Attribute").Contains("Except: Edge"),
                "Nested attribute choice must retain its maximum and Edge exclusion.");
            Require(Effect("Will to Live").Contains("Additional overflow boxes: 1"),
                "Overflow boxes must not be confused with damage resistance.");
            Require(Effect("Magic Resistance").StartsWith("Each level adds one die when resisting spells.", StringComparison.Ordinal)
                && Effect("Magic Resistance").Contains("Spell resistance: 1"),
                "Magic Resistance needs a per-level spell-resistance explanation, not a spellcasting bonus.");
            Require(Effect("Resistance to Pathogens/Toxins").StartsWith("Add two dice", StringComparison.Ordinal)
                && Effect("Resistance to Pathogens and Toxins").StartsWith("Add one die", StringComparison.Ordinal)
                && Effect("Resistance to Pathogens and Toxins").Contains("not the sum of all listed routes"),
                "The two distinct resistance sources must keep their exact values and must not sum alternative exposure routes.");
            Require(Effect("Born Rich").Contains("increases by 30") && Effect("Born Rich").Contains("still pay the Karma")
                && Effect("Out For Myself").Contains("three extra dice on Surprise tests"),
                "Readable explanations must retain an increased exchange limit and surprise dice, not free Karma or Initiative.");
            foreach (string name in new[] { "The Beast's Way", "The Spiritual Way", "The Burnout's Way", "The Magician's Way",
                "Changeling (Class I SURGE)", "Changeling (Class II SURGE)", "Changeling (Class III SURGE)",
                "Black Market Pipeline", "Erased", "Fame: Local", "Fame: National", "Fame: Megacorporate", "Fame: Global",
                "Made Man", "Ex-Con" })
            {
                string text = Effect(name);
                Require(!text.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", ""))
                    && text.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                    "Special-rule summaries must explain supported effects without pretending their unencoded rules are complete: " + name);
            }
            Require(Effect("Erased").StartsWith("Your total Public Awareness is capped at 1.", StringComparison.Ordinal)
                && Effect("Erased").Contains("A lower value stays lower"),
                "Erased caps Public Awareness; it does not set it to one or reset all reputation.");
            Require(Effect("The Beast's Way").Contains("If you choose Mentor Spirit")
                && Effect("The Beast's Way").Contains("one die to Animal Handling")
                && Effect("The Spiritual Way").Contains("one die to tests using the Conjuring skill group"),
                "A free-quality cost waiver is not an automatic mentor grant, and the two Ways have different skill bonuses.");
            Require(Effect("The Burnout's Way").Contains("80% of their normal Essence")
                && Effect("The Burnout's Way").Contains("not a price discount")
                && Effect("The Magician's Way").Contains("amount belongs to each power"),
                "Adept Ways must not invent a blanket money or Power Point discount.");
            foreach (string name in new[] { "Changeling (Class I SURGE)", "Changeling (Class II SURGE)", "Changeling (Class III SURGE)" })
                Require(Effect(name).Contains("separate 30-Karma limit") && Effect(name).Contains("not 30 extra Karma"),
                    "The metagenic allowance must not be presented as general-purpose bonus Karma.");
            Require(Effect("Black Market Pipeline").Contains("Eligible purchases in that category receive a 10% price discount")
                && Effect("Made Man").Contains("group contact with Loyalty fixed at 3")
                && Effect("Ex-Con").Contains("also gain SINner (Criminal)"),
                "Contact and criminal-SIN explanations must retain their exact category, fixed Loyalty and grant boundaries.");
            Require(Effect("Fame: Local").Contains("+1 to your Social limit in one chosen sprawl")
                && Effect("Fame: National").Contains("national-language rating is at least 4")
                && Effect("Fame: Megacorporate").Contains("+2 to your Social limit in one chosen megacorporation")
                && Effect("Fame: Global").Contains("Public Awareness also increases by 8"),
                "Fame variants must retain their distinct Social-limit scopes and reputation penalties.");
            Require(Effect("Quick Healer").Contains("Heal") && Effect("Quick Healer").Contains("Modifier: 2"),
                "Spell-specific healing modifier was lost.");
            Require(Effect("Uneducated").Contains("Cannot default")
                && Effect("Uneducated").Contains("Percent of normal cost: 200")
                && Effect("Uneducated").Contains("Specialization Karma cost"),
                "Skill restrictions and double training costs must be distinguished from bonus dice.");
            Require(Effect("Jack of All Trades Master of None").Contains("After character creation")
                && Effect("Jack of All Trades Master of None").Contains("Maximum: 5")
                && Effect("Jack of All Trades Master of None").Contains("Minimum: 6")
                && !Effect("Jack of All Trades Master of None").Contains("/character/"),
                "Karma cost conditions and rating boundaries must be retained in readable language.");
            Require(Effect("Sensitive System").Contains("Cyberware Essence cost (% of normal): 200")
                && Effect("Sensitive System").Contains("Cannot use bioware"),
                "Essence multipliers and the bioware restriction were lost.");
            Require(Effect("Dependent (Nuisance)").Contains("Lifestyle cost change (%): 10"),
                "A lifestyle percentage must not be presented as nuyen or dice.");
            Require(Effect("Celerity").Contains("Replacement walking multiplier")
                && Effect("Celerity").Contains("Multiplier: 3")
                && Effect("Celerity").Contains("Replacement running multiplier")
                && Effect("Celerity").Contains("Multiplier: 6")
                && Effect("Celerity").Contains("Meters per hit: 1")
                && !Effect("Celerity").Contains("100"), "Replacement rates and extra sprint meters must not become 100 dice or a percentage.");
            Require(Effect("Satyr Legs").Contains("Meters per hit: 1")
                && !Effect("Satyr Legs").Contains("Replacement walking multiplier"),
                "Satyr Legs must not borrow Celerity's walking benefit.");
            Require(Effect("Consummate Professional").Contains("Extra earned Karma needed per Street Cred: 10")
                && Effect("Consummate Professional").Contains("per 20 Karma"),
                "Street Cred's extra Karma divisor is not a reputation multiplier.");
            Require(Effect("Resonant Burnout").Contains("Essence-related special-attribute loss (% of normal): 20"),
                "Reduced attribute loss must not be described as cheaper augmentation Essence.");
            Require(Effect("Barehanded Adept").Contains("Based on attribute: Magic")
                && Effect("Barehanded Adept").Contains("Half the rating, rounded up; touch range only")
                && Effect("Barehanded Adept").Contains("Permitted spell range: Touch (area)"),
                "Free spells must retain their Magic, rounding and Touch restrictions.");
            Require(Effect("Dedicated Spellslinger").Contains("Based on skill: Spellcasting")
                && Effect("Dedicated Spellslinger").Contains("Unavailable skill: Summoning")
                && Effect("Dedicated Spellslinger").Contains("Unavailable skill: Binding"),
                "Skill-based free spells must retain their unavailable magic skills.");
            string deadSin = Effect("Dead SIN");
            Require(deadSin.Contains("Granted equipment · Fake SIN") && deadSin.Contains("Rating: 3")
                && Regex.Matches(deadSin, "Included equipment · Fake License").Count == 4,
                "A granted SIN and four identical licenses must retain all four child items and their ratings.");
            string banshee = Effect("Infected: Banshee");
            Require(banshee.Contains("Granted power: Dual Natured") && banshee.Contains("Granted power: Allergy")
                && banshee.Contains("Fixed detail: Sunlight, Severe")
                && banshee.Contains("Choose from these powers, not all of them")
                && banshee.Contains("Number of choices: 1") && banshee.Contains("Power option: Enhanced Senses")
                && banshee.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Granted powers, selected details and optional choices must be distinguished; power names are not full rules.");
            Require(Effect("Electroception (Electrosense)").Contains("Additional specialization option, not automatically learned")
                && Effect("Electroception (Electrosense)").Contains("Specialization: Electroception")
                && Effect("Electroception (Electrosense)").Contains("Skill: Perception"),
                "A new specialization option must not be confused with automatically learning it.");
            string inspired(string id) => string.Join(" ", CreationQualityInfo.Effects(
                catalog.Single(quality => quality.Element("id")!.Value == id).ToString()));
            Require(inspired("fd9b9b6d-c969-40f1-8dc7-61f8e5d9cd4d").Contains("Choose a free expertise specialization")
                && !inspired("f8f216b5-1c29-467d-9fb5-c9812408203d").Contains("Choose a free expertise specialization"),
                "Same-name qualities must not share source-identity summaries or expertise effects.");
            foreach (var (attribute, name, impairedId, metagenicId, optimizedId) in new[]
            {
                ("BOD", "Body", "96911a4d-d82e-4a1e-a550-3e12b8e14a7b", "2ffd990c-ced6-4484-955c-108473df5335", "3109f474-0d10-4c75-bc07-ef22afdd92ab"),
                ("AGI", "Agility", "8a1a04ff-5bff-48b2-90c2-b9bdb5fde8e8", "96c4c8e1-2f5d-431c-96c8-db83820f747b", "c42cc305-3d7a-4c68-9ce7-32e1fc628505"),
                ("REA", "Reaction", "959b00a6-a55b-4c6b-95b0-a9746b31a24d", "c734c06d-c9e4-431d-93ba-033e82444efc", "8c667328-eba0-4dc7-be5a-19c285997f10"),
                ("STR", "Strength", "c00591dc-fe6d-4ff0-8e42-22000878f03f", "76ac03f1-b379-48a8-a6e8-d8611fce0adf", "a6b946e2-7e58-4607-b9ce-7b27055c9562"),
                ("CHA", "Charisma", "14e71856-7d38-4e04-933f-069ed89a14e9", "f1609199-e601-4b69-837f-2029b2ea94f0", "7022143c-3d58-4ad7-9cdb-dab007f75f54"),
                ("INT", "Intuition", "b58b47a7-9011-4bef-8ba2-ae561a443765", "7ee0765f-34f4-43ef-82c0-55c1b8bef8d8", "22a991a5-e8b1-4ca7-98b9-3181df5dbf7e"),
                ("LOG", "Logic", "0df94dda-5941-4130-b6fc-0a98fcf7f846", "bfdffed5-b687-4cb4-ad6a-ac59788fdc8a", "62035d8e-c784-450e-9b03-501281a6771a"),
                ("WIL", "Willpower", "a6a4d71d-452f-4d26-9588-e7a5bc2474c0", "a6536e32-5f2f-4865-8f6a-d2ba268d4647", "96ae02da-5824-4ebd-a0c5-65bb5b43fd48")
            })
            {
                var impaired = catalog.Single(quality => quality.Element("id")!.Value == impairedId);
                var metagenic = catalog.Single(quality => quality.Element("id")!.Value == metagenicId);
                var optimized = catalog.Single(quality => quality.Element("id")!.Value == optimizedId);
                foreach (var quality in new[] { impaired, metagenic, optimized })
                    Require(quality.Element("bonus")!.Element("specificattribute")!.Element("name")!.Value == attribute,
                        "An attribute explanation must bind to the matching source attribute, not merely a similar name.");
                Require(impaired.Element("bonus")!.Element("specificattribute")!.Element("max")!.Value == "-2"
                    && impaired.Element("bonus")!.Element("specificattribute")!.Element("min") is null
                    && inspired(impairedId).StartsWith($"Your natural maximum for {name} is reduced by 2.", StringComparison.Ordinal),
                    "Impaired Attribute lowers the maximum, not every roll or every attribute.");
                Require(metagenic.Element("bonus")!.Element("specificattribute")!.Element("max")!.Value == "1"
                    && metagenic.Element("bonus")!.Element("specificattribute")!.Element("min")!.Value == "1"
                    && metagenic.Element("required")!.Descendants("quality").Count() == 3
                    && inspired(metagenicId).StartsWith($"Your natural {name} range shifts up by 1:", StringComparison.Ordinal)
                    && inspired(metagenicId).Contains("both its minimum and maximum increase")
                    && inspired(metagenicId).Contains("requires a SURGE changeling trait"),
                    "Metagenic Improvement raises both bounds and retains the changeling prerequisite.");
                Require(optimized.Element("bonus")!.Element("specificattribute")!.Element("max")!.Value == "1"
                    && optimized.Element("bonus")!.Element("specificattribute")!.Element("min") is null
                    && optimized.Element("chargenonly") is not null
                    && optimized.Element("forbidden")!.Descendants("bioware").Single().Value == $"Genetic Optimization ({name})"
                    && inspired(optimizedId).StartsWith($"Your natural maximum for {name} increases by 1.", StringComparison.Ordinal)
                    && inspired(optimizedId).Contains("not a free attribute point")
                    && inspired(optimizedId).Contains("during creation only")
                    && inspired(optimizedId).Contains("same attribute"),
                    "Genetic Optimization raises only the ceiling, costs points to use, and keeps creation/bioware restrictions.");
            }
            Require(inspired("4e2ddf3d-802f-4206-85ce-81f1defa528f").StartsWith(
                    "Your Mental limit increases by 1 for Academic Knowledge tests.", StringComparison.Ordinal)
                && !inspired("4e2ddf3d-802f-4206-85ce-81f1defa528f").Contains("half")
                && inspired("604aea10-3f13-4f28-a87b-25b8bf677276").StartsWith(
                    "During creation, Academic Knowledge skills use half the normal point cost;", StringComparison.Ordinal)
                && inspired("604aea10-3f13-4f28-a87b-25b8bf677276").Contains("Karma specialization costs are also halved")
                && inspired("604aea10-3f13-4f28-a87b-25b8bf677276").Contains("rating 3 or higher costs 1 less Karma per advancement")
                && !inspired("604aea10-3f13-4f28-a87b-25b8bf677276").Contains("Mental limit increases"),
                "The two College Education identities have distinct limit versus training-cost rules.");
            foreach (string name in new[] { "School of Hard Knocks", "Technical School Education" })
            {
                var quality = catalog.Single(quality => quality.Element("name")!.Value == name);
                var bonus = quality.Element("bonus")!;
                Require(bonus.Element("skillcategorypointcostmultiplier")!.Element("val")!.Value == "50"
                    && bonus.Element("skillcategorykarmacost")!.Element("val")!.Value == "-1"
                    && bonus.Element("skillcategorykarmacost")!.Element("min")!.Value == "3"
                    && bonus.Element("skillcategorykarmacost")!.Element("condition")!.Value == "/character/created"
                    && bonus.Element("skillcategorykarmacostmultiplier") is null
                    && bonus.Element("skillcategoryspecializationkarmacostmultiplier") is null
                    && Effect(name).Contains("rating 3 or higher costs 1 less Karma per advancement")
                    && Effect(name).Contains("does not halve creation-time Karma purchases or Karma specialization costs"),
                    "Street/Professional discounts must not inherit the different Academic Karma discounts.");
            }
            Require(Effect("Incompetent").StartsWith("Choose one skill group that is unavailable to you.", StringComparison.Ordinal)
                && Effect("Incompetent").Contains("Notoriety also increases by 1")
                && Effect("Impassive").StartsWith("Your Social limit is 1 lower, except on Intimidation tests.", StringComparison.Ordinal),
                "Restrictions must retain the selected group, reputation penalty and Intimidation exception.");
            foreach (string name in new[]
            {
                "Animal Pelage (Insulating Pelt)",
                "Arcane Arrester",
                "Balance Receptor",
                "Beak",
                "Raptor Beak",
                "Dermal Alteration (Bark Skin)",
                "Dermal Alteration (Blubber)",
                "Dermal Alteration (Dragon Skin)",
                "Dermal Alteration (Granite Shell)",
                "Dermal Alteration (Rhino Hide)",
                "Dermal Deposits",
                "Elongated Limbs",
                "Functional Tail (Balance)",
                "Functional Tail (Paddle)",
                "Functional Tail (Prehensile)",
                "Magnetoception",
                "Ogre Stomach",
                "Photometabolism",
                "Thorns",
                "Vomeronasal Organ",
                "Webbed Digits",
                "Adiposis",
                "Deformity (Quasimodo)",
                "Neoteny",
                "Progeria",
                "Slow Healer",
                "Stubby Arms",
                "Social Appearance Anxiety",
                "Elevated Stress",
                "Sloppy Code",
                "Better on the Net [Attack]",
                "Brittle [Attack]",
                "Better on the Net [Data Processing]",
                "Brittle [Data Processing]",
                "Better on the Net [Firewall]",
                "Brittle [Firewall]",
                "Better on the Net [Sleaze]",
                "Brittle [Sleaze]"
            })
            {
                var quality = catalog.Single(quality => quality.Element("name")!.Value == name);
                string summary = CreationFlowStrings.Get("Qualities.Summary." + quality.Element("id")!.Value, "");
                Require(summary.Length > 40 && Effect(name).StartsWith(summary, StringComparison.Ordinal),
                    "Physical and Matrix traits need their own source-bound explanations: " + name);
            }
            foreach (var (name, field, improvedId, brittleId) in new[]
            {
                ("Attack", "attack", "522dbfb5-5ea0-4707-a7e8-1ac777b3aff7", "d40aee6b-80b7-43f5-b335-dabf8f1ad14d"),
                ("Data Processing", "dataprocessing", "39139f84-b8ab-4523-ac02-851350e3f2b6", "64d430b1-6d6a-4abc-9ef8-3ddaee2352ce"),
                ("Firewall", "firewall", "25d34c52-6927-44dd-85bd-684e9add1f89", "a1530804-a35b-4d2f-ae3e-50d647016903"),
                ("Sleaze", "sleaze", "59436ccc-5e4b-481d-8d63-d3ba912f2d8a", "cb5bd1e5-b9ef-434f-af17-0e7b4decaa46")
            })
            {
                foreach (var (id, amount, direction) in new[] { (improvedId, "2", "increases"), (brittleId, "-1", "decreases") })
                {
                    var quality = catalog.Single(quality => quality.Element("id")!.Value == id);
                    var persona = quality.Element("bonus")!.Element("livingpersona")!;
                    Require(persona.Elements().Count() == 1 && persona.Element(field)!.Value == amount
                        && quality.Element("required")!.Descendants("quality").Single().Value == "Technomancer"
                        && inspired(id).StartsWith($"Your living persona's {name} {direction} by {(amount == "-1" ? "1" : amount)}.", StringComparison.Ordinal)
                        && inspired(id).Contains("requires a technomancer"),
                        "Matrix help must preserve one exact attribute, direction, magnitude and technomancer requirement.");
                }
            }
            Require(Effect("Animal Pelage (Insulating Pelt)").Contains("4 to armor against cold")
                && Effect("Dermal Alteration (Blubber)").Contains("2 to armor against cold")
                && Effect("Dermal Alteration (Dragon Skin)").Contains("2 to armor against fire"),
                "Cold and fire armor must not become universal armor or interchangeable elemental protection.");
            foreach (var (name, value) in new[] { ("Dermal Alteration (Bark Skin)", "2"),
                ("Dermal Alteration (Granite Shell)", "4"), ("Dermal Alteration (Rhino Hide)", "3") })
            {
                var quality = catalog.Single(quality => quality.Element("name")!.Value == name);
                Require(quality.Element("bonus")!.Element("armor")!.Value == value
                    && quality.Element("bonus")!.Element("armor")!.Attribute("group")!.Value == "0"
                    && Effect(name).Contains($"{value}-point armor modifier")
                    && Effect(name).Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                    "Grouped armor may be explained, but its unexpanded stacking flag must still be marked partial.");
            }
            Require(Effect("Arcane Arrester").Contains("Each level adds two dice when resisting spells, up to two levels")
                && Effect("Arcane Arrester").Contains("cannot be combined with Magic Resistance")
                && Effect("Functional Tail (Balance)").Contains("one die to Gymnastics")
                && Effect("Functional Tail (Paddle)").Contains("two dice to Swimming")
                && Effect("Functional Tail (Prehensile)").Contains("one die to Gymnastics")
                && Effect("Vomeronasal Organ").Contains("based on smell"),
                "Spell resistance, alternative tail variants and scent bonuses must retain their distinct scopes.");
            Require(Effect("Beak").Contains("lifestyle costs by 10%")
                && Effect("Beak").Contains("one die when resisting ingested toxins")
                && Effect("Ogre Stomach").Contains("lifestyle costs by 20%")
                && Effect("Ogre Stomach").Contains("two dice when resisting ingested toxins")
                && Effect("Raptor Beak").Contains("attack details are not yet described here")
                && Effect("Raptor Beak").Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Digestion bonuses must retain their cost/resistance differences, and weapon references stay incomplete.");
            Require(Effect("Adiposis").Contains("replacement multipliers of 1 for walking and 2 for running")
                && Effect("Adiposis").Contains("0.5 meters per hit")
                && Effect("Adiposis").Contains("Physical Active skill tests also lose one die")
                && Effect("Thorns").Contains("unarmed damage by 1, but Physical Active skill tests lose one die"),
                "Replacement movement rates and damage bonuses must not hide their skill penalties.");
            Require(Effect("Deformity (Quasimodo)").Contains("except Perception")
                && Effect("Neoteny").Contains("Physical condition monitor has two fewer boxes")
                && Effect("Neoteny").Contains("lifestyle costs increase by 10%")
                && Effect("Slow Healer").Contains("do not add both penalties to one recovery test"),
                "Physical drawbacks must preserve exclusions, monitor type and alternative healing rolls.");
            Require(Effect("Social Appearance Anxiety").Contains("one die per quality level, up to three levels")
                && Effect("Social Appearance Anxiety").Contains("When you are not looking your best")
                && Effect("Elevated Stress").Contains("alternative situations, not eight penalties to combine")
                && Effect("Sloppy Code").StartsWith("While inside a Matrix host,", StringComparison.Ordinal),
                "Conditional and rated penalties must not be advertised as unconditional combined totals.");
            var prototype = catalog.Single(quality => quality.Element("name")!.Value == "Prototype Transhuman");
            Require(prototype.Element("chargenonly") is not null
                && prototype.Element("bonus")!.Element("prototypetranshuman")!.Value == "1"
                && prototype.Element("bonus")!.Element("selectquality")!.Elements("quality").Select(choice => choice.Value)
                    .SequenceEqual(new[] { "Wanted", "Allergy (Common, Mild)", "Astral Beacon", "Insomnia (Basic)" })
                && Effect("Prototype Transhuman").Contains("Up to 1 Essence worth of bioware")
                && Effect("Prototype Transhuman").Contains("bioware still costs nuyen")
                && Effect("Prototype Transhuman").Contains("without gaining extra Karma")
                && Effect("Prototype Transhuman").Contains("not a general Essence discount"),
                "Prototype help must preserve the creation-only allowance and mandatory drawback, not free augmentation purchases.");
            var chimera = catalog.Single(quality => quality.Element("name")!.Value == "Wildcard Chimera");
            var chimeraChoices = chimera.Element("bonus")!.Element("selectquality")!;
            Require(chimeraChoices.Elements("quality").Count() == 17
                && chimeraChoices.Element("discountqualities")!.Elements("quality").Count() == 13
                && chimera.Element("required")!.Descendants("quality").All(quality => quality.Value.StartsWith("Infected:", StringComparison.Ordinal))
                && Effect("Wildcard Chimera").Contains("choice of one optional infected power")
                && Effect("Wildcard Chimera").Contains("alternatives, not powers you receive together")
                && Effect("Wildcard Chimera").Contains("zero cost does not promise a free power"),
                "Chimera help must distinguish one referenced power, optional drawbacks and unresolved final cost.");
            var technoshaman = catalog.Single(quality => quality.Element("name")!.Value == "Resonant Stream: Technoshaman");
            var cyberadept = catalog.Single(quality => quality.Element("name")!.Value == "Resonant Stream: Cyberadept");
            foreach (var stream in new[] { technoshaman, cyberadept })
                Require(stream.Element("required")!.Descendants("quality").Single().Value == "Technomancer"
                    && stream.Element("forbidden")!.Descendants("quality").Count() == 6,
                    "Stream explanations must retain the technomancer prerequisite and incompatible streams.");
            Require(technoshaman.Element("bonus")!.Elements().Single().Name.LocalName == "allowspritefettering"
                && Effect("Resonant Stream: Technoshaman").Contains("permanently fetter one sprite")
                && Effect("Resonant Stream: Technoshaman").Contains("does not grant a free sprite")
                && Effect("Resonant Stream: Technoshaman").Contains("In Career, fettering costs Karma equal to the sprite's rating"),
                "Fettering permission must not be advertised as unlimited free sprites.");
            var cyberadeptSkills = cyberadept.Element("bonus")!.Elements("specificskill").ToArray();
            Require(cyberadept.Element("bonus")!.Element("cyberadeptdaemon") is not null
                && cyberadeptSkills.Length == 4 && cyberadeptSkills.All(skill => skill.Element("bonus")!.Value == "2")
                && cyberadeptSkills.Select(skill => skill.Element("condition")!.Value).Distinct().OrderBy(value => value)
                    .SequenceEqual(new[] { "Companion Sprite", "Fault Sprite" })
                && cyberadeptSkills.Select(skill => skill.Element("name")!.Value).Distinct().OrderBy(value => value)
                    .SequenceEqual(new[] { "Compiling", "Decompiling" })
                && Effect("Resonant Stream: Cyberadept").Contains("not a four-die bonus")
                && Effect("Resonant Stream: Cyberadept").Contains("subject to the character's current limits and rules settings")
                && Effect("Resonant Stream: Cyberadept").Contains("does not refund Essence or undo bioware loss"),
                "Cyberadept help must preserve alternative sprite targets and conditional Resonance recovery.");
            string Describe(string bonus) => string.Join(" ", CreationQualityInfo.Effects("<quality><bonus>" + bonus + "</bonus></quality>"));
            foreach (string effect in new[] { "<allowspritefettering />", "<cyberadeptdaemon />",
                "<prototypetranshuman>9</prototypetranshuman>", "<selectquality><quality>Unknown</quality></selectquality>" })
                Require(Describe(effect) == CreationFlowStrings.Get("Qualities.Info.Manual", ""),
                    "Curated source-identity summaries must not invent mechanics for an unrelated or unknown source.");
            foreach (string name in new[] { "Animal Empathy", "City Slicker", "Outdoorsman", "Sense of Direction",
                "Vehicle Empathy", "Water Sprite", "Computer Illiterate", "Loss of Confidence", "Nasty Vibe",
                "Grease Monkey", "Alibi", "Closer", "Innocuous", "Memory Palace", "Hi-Rez",
                "Tough as Nails (Physical)", "Tough as Nails (Stun)", "Reduced Sense (Smell)",
                "Reduced Sense (Taste)", "Reduced Sense (Touch)", "Reduced Sense (Hearing)",
                "Reduced Sense (Sight)", "Reduced Sense (Astral Sight)" })
            {
                var quality = catalog.Single(quality => quality.Element("name")!.Value == name);
                string summary = CreationFlowStrings.Get("Qualities.Summary." + quality.Element("id")!.Value, "");
                Require(summary.Length > 40 && Effect(name).StartsWith(summary, StringComparison.Ordinal),
                    "A supported skill or monitor effect needs its own source-bound explanation: " + name);
            }
            Require(Effect("City Slicker").Contains("other than Survival")
                && Effect("City Slicker").Contains("general one-die penalty")
                && Effect("City Slicker").Contains("Outside urban areas, Perception loses one die")
                && Effect("Outdoorsman").Contains("alternative environments, not six bonuses added together"),
                "Environmental bonuses must retain their exceptions and penalties, not stack alternative conditions.");
            Require(Effect("Vehicle Empathy").Contains("except Gunnery")
                && Effect("Water Sprite").Contains("two dice to Diving tests and two dice to Swimming tests")
                && Effect("Loss of Confidence").Contains("rating of at least 4")
                && Effect("Loss of Confidence").Contains("specialization bonuses do not apply")
                && Effect("Computer Illiterate").Contains("skill ratings stay unchanged"),
                "Skill help must preserve exclusions, selection restrictions and dice versus learned ratings.");
            Require(Effect("Alibi").Contains("plausible-seeming evidence")
                && Effect("Closer").Contains("life or death") && Effect("Innocuous").Contains("hiding in a crowd")
                && Effect("Hi-Rez").Contains("Other Computer tests do not gain this bonus"),
                "Conditional skill bonuses must not be advertised as unconditional.");
            Require(Effect("Tough as Nails (Physical)").StartsWith("Each level adds one box to your Physical condition monitor.", StringComparison.Ordinal)
                && Effect("Tough as Nails (Stun)").StartsWith("Each level adds one box to your Stun condition monitor.", StringComparison.Ordinal)
                && Effect("Reduced Sense (Sight)").Contains("rely on sight")
                && Effect("Reduced Sense (Astral Sight)").Contains("Assensing tests"),
                "Physical versus Stun boxes and visual versus astral perception must stay distinct.");
            Require(Effect("Frostbite") == CreationFlowStrings.Get("Qualities.Info.Manual", "")
                && Describe("<selectskill limittoskill='Hacking' />") == CreationFlowStrings.Get("Qualities.Info.Manual", "")
                && Describe("<selectskill><val> </val></selectskill>") == CreationFlowStrings.Get("Qualities.Info.Manual", ""),
                "A skill-selection prompt with no modifier must not pass for a rule explanation.");
            Require(Effect("Aptitude").Contains("Maximum change: 1")
                && Describe("<selectskill><disablespecializationeffects /></selectskill>").Contains("Specialization bonuses do not apply"),
                "Maximum-only and specialization-only changes are real effects even without bonus dice.");
            Require(Effect("Bad Luck").Contains("Notoriety: 1")
                && Effect("Bad Luck").Contains("Whenever you spend Edge")
                && Effect("Bad Luck").Contains("only once per session")
                && Effect("Bad Luck").Contains("stop making this check")
                && !Effect("Bad Luck").Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Bad Luck must explain its limited Edge reversal as well as its reputation effect.");
            Require(Describe("<notoriety>1</notoriety>").Contains(CreationFlowStrings.Get("Qualities.Info.Additional", ""))
                && Describe("<publicawareness>2</publicawareness>").Contains(CreationFlowStrings.Get("Qualities.Info.Additional", ""))
                && Describe("<astralreputation>1</astralreputation><selectskill />").Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Reputation side effects must remain visible but must not masquerade as the full quality rules.");
            Require(Effect("Astral Beacon").Contains("linger twice as long")
                && Effect("Astral Beacon").Contains("one fewer hit")
                && Effect("Astral Beacon").Contains("not their dice pool")
                && Effect("Distinctive Style").Contains("minimum one")
                && Effect("Distinctive Style").Contains("Astral searches are unaffected"),
                "Astral and physical identifiability must keep their scopes and thresholds distinct from dice modifiers.");
            Require(Effect("Combat Paralysis").Contains("halve your first Initiative score, rounding up")
                && Effect("Combat Paralysis").Contains("Later Initiative tests are normal")
                && Effect("Combat Paralysis").Contains("Surprise tests lose three dice")
                && Effect("Combat Paralysis").Contains("Composure tests under fire or in combat need one extra hit"),
                "Combat Paralysis must retain opening-score, Surprise and Composure effects without halving all later Initiative.");
            Require(Effect("Insomnia (Basic)").Contains("doubles each recovery interval")
                && Effect("Insomnia (Full)").Contains("prevents any healing from that rest attempt")
                && Effect("Insomnia (Basic)") != Effect("Insomnia (Full)"),
                "The two sleep-related drawbacks must distinguish slower recovery from a failed recovery attempt.");
            Require(Effect("Codeblock").Contains("one Matrix action that requires a test")
                && Effect("Codeblock").Contains("Other Matrix actions are unaffected")
                && Effect("Simsense Vertigo").Contains("smartlinks, simrigs and image links")
                && Effect("Low Pain Tolerance").Contains("every two filled boxes instead of every three"),
                "Interface penalties and earlier wound penalties must not become universal test penalties or smaller monitors.");
            Require(Effect("Elf Poser").Contains("human-only")
                && Effect("Ork Poser").Contains("human or elf")
                && Effect("Ork Poser").Contains("do not gain ork attributes")
                && Effect("Spirit Bane").Contains("one spirit type")
                && Effect("Spirit Bane").Contains("it gains two dice against your Banishing")
                && Effect("Spirit Bane").Contains("Watchers and other constructs do not count"),
                "Imitating a metatype must not grant attributes; spirit hostility must retain type scope and who rolls the bonus.");
            foreach (string frequency in new[] { "Uncommon", "Common" })
            {
                string mild = Effect($"Allergy ({frequency}, Mild)");
                string moderate = Effect($"Allergy ({frequency}, Moderate)");
                string severe = Effect($"Allergy ({frequency}, Severe)");
                string extreme = Effect($"Allergy ({frequency}, Extreme)");
                Require(mild.Contains("Physical tests lose two dice")
                    && mild.Contains("using that allergen loses one die")
                    && moderate.Contains("Physical tests lose four dice")
                    && moderate.Contains("using that allergen loses two dice"),
                    "Mild/moderate allergies affect Physical tests and retain separate allergen-attack resistance penalties.");
                Require(severe.Contains("all tests lose four dice")
                    && severe.Contains("each minute causes one unresisted Physical damage box")
                    && severe.Contains("using the allergen loses three dice")
                    && extreme.Contains("all actions lose six dice")
                    && extreme.Contains("every 30 seconds")
                    && extreme.Contains("resistance loses four dice")
                    && extreme.Contains("First Aid, Medicine or magic"),
                    "Higher allergy grades must retain all-test scope, unresisted damage intervals and extreme-shock treatment.");
                Require(!mild.Contains("unresisted") && !moderate.Contains("unresisted")
                    && !severe.Contains("30 seconds") && !extreme.Contains("each minute"),
                    "Lower allergy grades must not borrow ongoing damage or another grade's interval.");
            }
            Require(Effect("Addiction (Mild)").Contains("Monthly craving: one dose or one hour")
                && Effect("Addiction (Mild)").Contains("lose two dice on Mental-based tests for psychological dependence")
                && Effect("Addiction (Moderate)").Contains("every two weeks: one dose or one hour")
                && Effect("Addiction (Moderate)").Contains("four dice on Mental-based tests for psychological dependence")
                && Effect("Addiction (Moderate)").Contains("successful withdrawal test avoids those symptoms"),
                "Mild/moderate addiction help must preserve craving frequency, conditional withdrawal and dependency scope.");
            Require(Effect("Addiction (Severe)").Contains("Weekly craving: two doses or two hours")
                && Effect("Addiction (Severe)").Contains("Social tests always lose two dice, even outside withdrawal")
                && Effect("Addiction (Burnout)").Contains("Daily craving: at least three doses or three hours")
                && Effect("Addiction (Burnout)").Contains("six fewer dice on Mental-based tests")
                && Effect("Addiction (Burnout)").Contains("Social tests always lose three dice"),
                "Severe/burnout addiction help must distinguish withdrawal penalties from the persistent social penalty.");
            foreach (string frequency in new[] { "Common", "Specific" })
            {
                foreach (var (degree, dice) in new[] { ("Biased", 2), ("Outspoken", 4), ("Radical", 6) })
                {
                    string prejudice = Effect($"Prejudiced ({frequency}, {degree})");
                    Require(prejudice.Contains($"Social tests with its members take -{dice} dice")
                        && prejudice.Contains($"when negotiating with you, they gain +{dice} dice")
                        && prejudice.Contains("not a penalty to every social interaction"),
                        "Prejudice severity must change both opponents' rolls only when interacting with the chosen group.");
                    Require(prejudice.Contains(frequency == "Common" ? "commonly encountered" : "more narrowly defined"),
                        "Target prevalence must remain distinct from the severity's dice modifiers.");
                }
            }
            Require(Effect("SINner (National)").Contains("15% of gross income")
                && Effect("SINner (National)").Contains("identity and biometrics")
                && Effect("SINner (National)").Contains("fake identity does not erase this record")
                && Effect("SINner (Criminal)").Contains("15% of gross income")
                && Effect("SINner (Criminal)").Contains("replaces any previous SIN")
                && Effect("SINner (Criminal)").Contains("Registered magic users also face checks")
                && Effect("SINner (Criminal)").Contains("Notoriety: 1"),
                "National/criminal SINs need their registry, replacement and oversight consequences beyond the encoded reputation modifier.");
            Require(Effect("SINner (Corporate Limited)").Contains("20% of gross income")
                && Effect("SINner (Corporate Limited)").Contains("not leadership or special-forces privileges")
                && Effect("SINner (Corporate Limited)").Contains("target you for extraction")
                && Effect("SINner (Corporate)").Contains("10% of gross income")
                && Effect("SINner (Corporate)").Contains("global registry only confirms SIN validity")
                && Effect("SINner (Corporate)").Contains("does not grant free corporate resources"),
                "Corporate SIN variants must not swap tax rates, disclose the same registry detail or imply free corporate equipment.");
            Require(!Describe("<notoriety>1</notoriety><memory>1</memory>").Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "A reputation modifier alongside a described primary effect is not a reputation-only explanation.");
            string gremlins = Effect("Gremlins");
            Require(gremlins.Contains("Once, at the first level only: Notoriety: 1")
                && gremlins.Contains("minimum of one") && gremlins.Contains("Implants are unaffected")
                && gremlins.Contains("cannot be used to sabotage")
                && !gremlins.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Gremlins needs its glitch rule and scope, not just a one-time reputation modifier.");
            string ratedGremlins = string.Join(" ", CreationQualityInfo.Effects(
                catalog.Single(quality => quality.Element("name")!.Value == "Gremlins").ToString(), 3));
            Require(ratedGremlins == gremlins,
                "The per-level glitch description must not multiply the one-time Notoriety effect.");
            var unknownGremlins = new System.Xml.Linq.XElement(catalog.Single(quality => quality.Element("name")!.Value == "Gremlins"));
            unknownGremlins.Element("id")!.Value = Guid.Empty.ToString("D");
            Require(CreationQualityInfo.Effects(unknownGremlins.ToString()).Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "An unrelated first-level-only definition still needs the incomplete-description warning.");
            foreach (string name in new[] { "Ambidextrous", "Astral Chameleon", "Blandness", "Focused Concentration",
                "Gearhead", "Guts", "Human-Looking", "Juryrigger", "Natural Hardening", "Gremlins",
                "Mentor Spirit", "Paragon", "Inherent Program" })
            {
                var quality = catalog.Single(quality => quality.Element("name")!.Value == name);
                string summary = CreationFlowStrings.Get(SummaryKey(quality), "");
                Require(summary.Length > 40 && Effect(name).StartsWith(summary, StringComparison.Ordinal),
                    "Core quality help must provide original source-bound prose: " + name);
            }
            Require(Effect("Astral Chameleon").Contains("twice as quickly")
                && Effect("Astral Chameleon").Contains("not your physical presence")
                && Effect("Blandness").Contains("Magical and Matrix searches are unaffected")
                && Effect("Blandness").Contains("stand out can remove the benefit"),
                "Concealment explanations must retain their different scopes and exceptions.");
            Require(Effect("Focused Concentration").Contains("one spell or complex form")
                && Effect("Focused Concentration").Contains("does not exceed this quality's rating")
                && Effect("Focused Concentration").Contains("does not waive Drain or Fading")
                && Effect("Guts").Contains("resisting fear or intimidation")
                && Effect("Guts").Contains("does not make you immune"),
                "Mental-discipline help must not grant unlimited sustaining, fear immunity or attack bonuses.");
            Require(Effect("Gearhead").Contains("20% more Speed or +1 Handling")
                && Effect("Gearhead").Contains("per extra minute")
                && Effect("Juryrigger").Contains("Results are temporary")
                && Effect("Juryrigger").Contains("burns out its critical components"),
                "Technical tricks must retain their alternatives, temporary duration and damage risk.");
            Require(Effect("Human-Looking").Contains("actual metatype and its attributes do not change")
                && Effect("Natural Hardening").Contains("one point of natural biofeedback filtering")
                && Effect("Natural Hardening").Contains("not ordinary physical attacks"),
                "Appearance and biofeedback protection must not imply altered metatype or general armor.");
            foreach (string name in new[] { "Mentor Spirit", "Paragon", "Inherent Program" })
                Require(Effect(name).Contains("not yet fully described here")
                    && Effect(name).Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                    "A guide/program choice is useful partial help, not the selected profile's full mechanics.");
            string infirm = Effect("Infirm");
            Require(Regex.Matches(infirm, "Once, at the first level only: Augmentations cannot raise this attribute above its natural maximum").Count == 4
                && Regex.Matches(infirm, "Maximum change: -1").Count == 4,
                "Infirm needs all four natural-maximum clamps as well as its four per-level maximum reductions.");
            string profile = "<naturalweapon><name>Test claw</name><reach>0</reach><damage>({STR}+1)P</damage><ap>-1</ap><useskill>Unarmed Combat</useskill><accuracy>Physical</accuracy><source>HIDDEN_BOOK</source><page>999</page></naturalweapon>";
            string DescribeSource(string body) => string.Join(" ", CreationQualityInfo.Effects("<quality>" + body + "</quality>"));
            var unknownRated = CreationQualityInfo.Effects("<quality><bonus><futureeffect>3</futureeffect></bonus></quality>", 3);
            Require(unknownRated.Count == 1 && unknownRated[0] == CreationFlowStrings.Get("Qualities.Info.Manual", ""),
                "A rating notice must not disguise missing rule descriptions as usable help.");
            string formulaRated = string.Join(" ", CreationQualityInfo.Effects(
                "<quality><bonus><spellresistance>Rating * 2</spellresistance></bonus></quality>", 3));
            Require(formulaRated.Contains("Spell resistance: Rating * 2") && !formulaRated.Contains("Spell resistance: 6"),
                "The help renderer must never evaluate rating expressions or invent a combined total.");
            string scoped = DescribeSource("<bonus><notoriety>1</notoriety></bonus><firstlevelbonus><notoriety>1</notoriety></firstlevelbonus>");
            Require(Regex.Matches(scoped, "Notoriety: 1").Count == 2
                && Regex.Matches(scoped, "Once, at the first level only").Count == 1,
                "Equal normal and first-level values have different scopes and must not be deduplicated together.");
            string weapons = DescribeSource("<naturalweapons>" + profile + profile + "</naturalweapons>");
            Require(Regex.Matches(weapons, "Natural weapon: Test claw").Count == 2
                && weapons.Contains("Damage formula: (Strength+1) Physical damage")
                && weapons.Contains("Armor penetration: -1") && weapons.Contains("Accuracy limit: Physical")
                && !weapons.Contains("HIDDEN_BOOK") && !weapons.Contains("999")
                && !weapons.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Natural weapons retain multiplicity and literal formulas but never source/page referrals.");
            Require(Describe(profile).Contains("Natural weapon: Test claw"),
                "A natural weapon can also be encoded inside the normal bonus node.");
            foreach (string partial in new[] {
                "<naturalweapons condition='unknown'>" + profile + "</naturalweapons>",
                "<naturalweapons>" + profile.Replace("</naturalweapon>", "<futurecondition>unknown</futurecondition></naturalweapon>") + "</naturalweapons>",
                "<naturalweapons>" + profile.Replace("<ap>-1</ap>", "") + "</naturalweapons>",
                "<naturalweapons>" + profile.Replace("<ap>-1</ap>", "<ap>-1</ap><ap>-2</ap>") + "</naturalweapons>",
                "<naturalweapons>" + profile.Replace("<ap>-1</ap>", "<ap condition='unknown'>-1</ap>") + "</naturalweapons>",
                "<firstlevelbonus condition='unknown'><notoriety>1</notoriety></firstlevelbonus>" })
                Require(DescribeSource(partial).Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                    "Unknown wrapper/weapon conditions and absent or ambiguous fields must remain visibly partial.");
            Require(DescribeSource("<naturalweapons><armor>2</armor></naturalweapons>") == CreationFlowStrings.Get("Qualities.Info.Manual", ""),
                "Only weapon definitions may be interpreted in the naturalweapons wrapper.");
            string reference = DescribeSource("<addweapon rating='2'>Named weapon</addweapon><addweapon>Named weapon</addweapon>");
            Require(Regex.Matches(reference, "Granted weapon; attack details not yet available: Named weapon").Count == 2
                && reference.Contains("Rating: 2") && reference.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", ""))
                && !reference.Contains("Damage formula"), "Weapon references are not full attack profiles or ambient-catalog authority.");
            Require(DescribeSource("<addweapon>00000000-0000-0000-0000-000000000001</addweapon>") == CreationFlowStrings.Get("Qualities.Info.Manual", ""),
                "Unresolved weapon identities must not leak into help.");
            Require(Effect("Crystalline Shards").Contains("Armor penetration: 4")
                && Effect("Crystalline Shards").Contains("Skill: Throwing Weapons")
                && Effect("Crystalline Shards").Contains("four more armor")
                && !Effect("Crystalline Shards").Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "The shards' positive armor modifier must not be inverted or confused with a damage bonus.");
            Require(Effect("Crystalline Blade").Contains("Reach: 1") && Effect("Crystalline Blade").Contains("Armor penetration: -2")
                && Effect("Crystalline Claws").Contains("Damage formula: (Strength+1) Physical damage"),
                "The distinct crystal weapon profiles must not borrow one another's damage, reach or armor values.");
            string dealer = Effect("Dealer Connection");
            Require(dealer.Contains("Choose one vehicle category for a 10% purchase discount")
                && dealer.Contains("Choose from: Drones") && dealer.Contains("Choose from: Groundcraft")
                && dealer.Contains("Choose from: Watercraft") && dealer.Contains("Choose from: Aircraft")
                && !dealer.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Dealer Connection must distinguish one selected discount category from all vehicle categories.");
            foreach (var (name, lifestyle) in new[] { ("Trust Fund I", "Medium"), ("Trust Fund II", "Low"),
                ("Trust Fund III", "High"), ("Trust Fund IV", "Medium") })
            {
                string trust = Effect(name);
                Require(trust.Contains("Lifestyle eligible for trust-fund support: " + lifestyle)
                    && trust.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                    "Trust Fund levels identify lifestyle eligibility, not cash amounts or complete income rules.");
            }
            string redliner = Effect("Redliner");
            Require(redliner.Contains("at most +2: Agility") && redliner.Contains("at most +2: Strength")
                && redliner.Contains("lose 3 boxes per eligible cyberlimb pair, at most 6")
                && !redliner.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Redliner's cyberlimb-dependent bonuses must include the Physical monitor penalty and both caps.");
            Require(Effect("Cyber-Singularity Seeker").Contains("at most +2: Willpower")
                && !Effect("Cyber-Singularity Seeker").Contains("lose 3 boxes"),
                "Cyber-Singularity Seeker must not borrow Redliner's monitor penalty.");
            Require(Effect("Overclocker").Contains("Add 1 to one chosen overclocked Matrix attribute")
                && !Effect("Overclocker").Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "An empty overclocker flag should explain its chosen-attribute benefit.");
            Require(Effect("Friends in High Places").Contains("Connection 8 or higher")
                && Effect("Friends in High Places").Contains("four times your Charisma"),
                "High-Connection contact points need their threshold and separate Charisma-based budget.");
            Require(Describe("<actiondicepool category='Matrix' />") == CreationFlowStrings.Get("Qualities.Info.Manual", ""),
                "An action-selection prompt alone must not invent Codeslinger's benefit without its definition-bound summary.");
            foreach (string malformed in new[] { "<trustfund>5</trustfund>", "<trustfund>Rating</trustfund>",
                "<cyberseeker>unknown</cyberseeker>", "<overclocker>unknown</overclocker>",
                "<dealerconnection />", "<dealerconnection><category>Unknown</category></dealerconnection>",
                "<dealerconnection><category>00000000-0000-0000-0000-000000000001</category></dealerconnection>" })
                Require(Describe(malformed) == CreationFlowStrings.Get("Qualities.Info.Manual", ""),
                    "Unknown enum values and absent/unresolved choice targets must not imply a known benefit.");
            Require(Describe("<dealerconnection><category>Drones</category><futurecondition>unknown</futurecondition></dealerconnection>")
                .Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "A known discount must not swallow an unknown dealer restriction.");
            Require(Describe("<overclocker futurecondition='unknown' />")
                .Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Unknown conditions on a known flag must remain visible as incomplete.");
            foreach (string conditional in new[] { "<overclocker condition='Only at night' />",
                "<cyberseeker condition='Only at night'>BOX</cyberseeker>", "<trustfund condition='Only at night'>1</trustfund>",
                "<dealerconnection condition='Only at night'><category>Drones</category></dealerconnection>" })
                Require(Describe(conditional).Contains("When: Only at night"),
                    "Known conditions on special benefits must be displayed, not merely accepted by validation.");
            string datahaven = Effect("Prime Datahaven Membership");
            Require(datahaven.Contains("Granted contact") && datahaven.Contains("Connection: 5")
                && datahaven.Contains("Base Loyalty: 1") && datahaven.Contains("Fixed Loyalty: 3")
                && datahaven.Contains("Group contact") && datahaven.Contains("Does not cost contact points"),
                "Granted group contact must preserve free cost, Connection and fixed versus base Loyalty.");
            string practice = Effect("Practice, Practice, Practice");
            Require(practice.Contains("Weapon Accuracy change, not bonus dice") && practice.Contains("Modifier: 1")
                && practice.Contains("Chosen skill · Except: Combat skills"),
                "Weapon Accuracy is not an attack-pool bonus; preserve the accepted skill exclusion.");
            string deathDealer = Effect("Death Dealer (Adept)");
            Require(deathDealer.Contains("Weapon damage change, not bonus dice") && deathDealer.Contains("Bonus: 1")
                && deathDealer.Contains("Choose from: Astral Combat,Blades,Clubs,Exotic Melee Weapon,Unarmed Combat")
                && deathDealer.Contains("Spell Drain change"), "Weapon damage's skill restriction and increased spell Drain must remain visible.");
            string chainBreaker = Effect("Chain Breaker");
            Require(Regex.Matches(chainBreaker, "Choose an additional summonable spirit type, not a summoned spirit").Count == 2
                && chainBreaker.Contains("Unavailable skill: Binding"), "Two spirit-type choices must not be deduplicated or described as summoned allies.");
            string conjurer = Effect("Dedicated Conjurer");
            Require(conjurer.Contains("Based on skill: Summoning")
                && conjurer.Contains("Number of choices: Base skill rating / 2 (Rounded down)")
                && conjurer.Contains("Unavailable skill: Spellcasting") && !conjurer.Contains("addtoselected"),
                "Dedicated Conjurer needs its full-increment skill basis without exposing bookkeeping XML.");
            string hedge = Effect("Hedge Witch/Wizard");
            Require(hedge.Contains("Spell choices restricted to one chosen category · Except: Rituals")
                && hedge.Contains("Additionally permitted spell category: Rituals"),
                "Excluding Rituals from the category choice must not hide that Rituals remain separately permitted.");
            Require(Effect("Elementalist (Fire)").Contains("Summoning restricted to one chosen spirit type · Choose from: Spirit of Fire")
                && Effect("Elementalist (Fire)").Contains("Unavailable skill group: Enchanting"),
                "Elementalist must show its spirit restriction and forbidden skill group.");
            Require(Describe("<selectcontact />") == CreationFlowStrings.Get("Qualities.Info.Manual", ""),
                "A contact selection prompt alone does not explain Sensei or another quality's rules.");
            string contact = Describe("<selectcontact><type>nongroup</type><forcedloyalty>Rating + 1</forcedloyalty><free /></selectcontact>");
            Require(contact.Contains("Chosen existing contact") && contact.Contains("Fixed Loyalty: Rating + 1")
                && contact.Contains("Does not cost contact points") && !contact.Contains("Granted contact"),
                "Changing an existing contact is not creating a new one; retain symbolic values.");
            Require(Describe("<addcontact />").Contains("Connection: 1") && Describe("<addcontact />").Contains("Base Loyalty: 1"),
                "Default contact values must not disappear when omitted from source XML.");
            string repeatedContacts = Describe("<addcontact /><addcontact />");
            Require(Regex.Matches(repeatedContacts, "Granted contact").Count == 2,
                "Two independent granted contacts must remain two displayed contacts.");
            string unknownWeaponCondition = Describe("<weaponskillaccuracy><value>1</value><selectskill limittoskill='Pistols' futurecondition='unknown' /></weaponskillaccuracy>");
            Require(unknownWeaponCondition.Contains("Choose from: Pistols")
                && unknownWeaponCondition.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Unknown nested weapon-choice conditions must not be silently dropped.");
            Require(Describe("<weaponskillaccuracy><selectskill /></weaponskillaccuracy>") == CreationFlowStrings.Get("Qualities.Info.Manual", ""),
                "A missing weapon modifier must not be guessed as a +1 bonus.");
            Require(Describe("<addspirit skill='Summoning' ratingdivisor='unknown' />") == CreationFlowStrings.Get("Qualities.Info.Manual", ""),
                "An unknown spirit-choice divisor must not be evaluated or invented.");
            string symbolicSpirit = Describe("<addspirit skill='Summoning' ratingdivisor='2'><futurecondition>unknown</futurecondition></addspirit>");
            Require(symbolicSpirit.Contains("Base skill rating / 2 (Rounded down)")
                && symbolicSpirit.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Known spirit-choice basis must not conceal an unknown condition.");
            string powers = Describe("<critterpowers><power rating='2'>Armor</power><power select='Fire'>Immunity</power></critterpowers>");
            Require(powers.Contains("Granted power: Armor · Rating: 2") && powers.Contains("Fixed detail: Fire"),
                "Granted power ratings and fixed selections cannot be dropped.");
            string optional = Describe("<optionalpowers count='2'><optionalpower>Armor</optionalpower><optionalpower>Fear</optionalpower></optionalpowers>");
            Require(optional.Contains("Number of choices: 2") && !optional.Contains("Granted power:"),
                "Optional power count is a choice count, not a grant of every listed power.");
            string gearPrice = Describe("<addgear><name>Test item</name><rating>Rating + 1</rating><quantity>2</quantity><fullcost /></addgear>");
            Require(gearPrice.Contains("Pay full price") && gearPrice.Contains("Quantity: 2")
                && gearPrice.Contains("Rating: Rating + 1") && !gearPrice.Contains("No nuyen cost"),
                "Full-cost grants must not be described as free; retain quantity and symbolic rating.");
            string hiddenGearCondition = Describe("<addgear><name>Test item</name><children><child><name>Child</name><futurecondition>unknown</futurecondition></child></children></addgear>");
            Require(hiddenGearCondition.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Unknown conditions on included equipment must keep the whole explanation partial.");
            string deeperGear = Describe("<addgear><name>Parent</name><children><child><name>Child</name><children><child><name>Not an admitted grandchild</name></child></children></child></children></addgear>");
            Require(!deeperGear.Contains("Not an admitted grandchild")
                && deeperGear.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Display must not grant recursively nested equipment beyond Core's immediate-child contract.");
            string unresolved = Describe("<addqualities><addquality>00000000-0000-0000-0000-000000000001</addquality></addqualities>");
            Require(!unresolved.Contains("00000000") && unresolved.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "An unresolved quality reference stays hidden and partial, not apparently complete.");
            string specialization = Describe("<selectexpertise limittoskill='Artisan' limittospecialization='Painting,Sculpture' />");
            Require(specialization.Contains("Choose from: Artisan") && specialization.Contains("Choose specialization from: Painting,Sculpture"),
                "Expertise must preserve both skill and specialization restrictions.");
            string sprint = Describe("<movementreplace><category>Fly</category><speed>sprint</speed><val>500</val></movementreplace>");
            Require(sprint.Contains("Replacement sprint distance") && sprint.Contains("Category: Flying")
                && sprint.Contains("Meters per hit: 5") && !sprint.Contains("500"), "Replacement sprint distance is also stored in hundredths.");
            string noCategory = Describe("<movementreplace><val>3</val></movementreplace>");
            Require(noCategory.Contains("Replacement walking multiplier") && noCategory.Contains("All movement types"),
                "A replacement without explicit speed/category defaults to walking for all movement types.");
            string mixedUnits = Describe("<sprintbonus><category>Ground</category><val>50</val><percent>25</percent></sprintbonus>");
            Require(mixedUnits.Contains("Meters per hit: 0.5") && mixedUnits.Contains("Percentage change: 25"),
                "Only the sprint-distance value uses hundredths; percentage changes must remain unchanged.");
            string symbolic = Describe("<sprintbonus><category>Ground</category><val>Rating * 100</val></sprintbonus>");
            Require(symbolic.Contains("Meters per hit: (Rating * 100) / 100"),
                "Symbolic unit conversion must remain an unevaluated expression.");
            string crystal = Describe("<essencepenaltyt100>-150</essencepenaltyt100><essencepenaltymagonlyt100>150</essencepenaltymagonlyt100>");
            Require(crystal.Contains("Essence change: -1.5") && crystal.Contains("Essence adjustment for Magic loss only: 1.5"),
                "Magic-specific Essence adjustment is not a grant of Magic attribute points.");
            string legacyInitiative = Describe("<initiativepass precedence='0'>1</initiativepass>");
            Require(legacyInitiative.Contains("Initiative dice: 1")
                && legacyInitiative.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Legacy initiative tag means dice, but unexplained stacking precedence remains partial.");
            Require(Describe("<movementreplace><speed>unknown</speed><val>500</val></movementreplace>")
                == CreationFlowStrings.Get("Qualities.Info.Manual", ""), "Unknown movement types must not be guessed as walking.");
            string fading = Describe("<fadingvalue specific='Resonance Spike'>-2</fadingvalue>");
            Require(fading.Contains("Fading value change: -2") && fading.Contains("Only for: Resonance Spike"),
                "A targeted Fading modifier must not appear to apply to every complex form.");
            string limit = Describe("<limitmodifier><limit>Mental</limit><value>1</value><condition>LimitCondition_SkillsKnowledgeAcademic</condition></limitmodifier>");
            Require(limit.Contains("Limit: Mental") && limit.Contains("Modifier: 1")
                && limit.Contains("Academic Knowledge") && !limit.Contains("LimitCondition_"),
                "Limit values and translated conditions were omitted.");
            string restricted = Describe("<selectskill minimumrating='4' limittoskill='Hacking'><val>-2</val><disablespecializationeffects /></selectskill>");
            Require(restricted.Contains("Minimum skill rating: 4") && restricted.Contains("Choose from: Hacking")
                && restricted.Contains("Specialization bonuses do not apply"), "Skill-choice restrictions were lost.");
            string partialText = Describe("<specificskill><name>Perception</name><bonus>1</bonus><futurecondition>unknown</futurecondition></specificskill>");
            Require(partialText.Contains("Bonus: 1") && partialText.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "An unknown field must produce a partial-description warning instead of disappearing silently.");
            string duplicatePartial = Describe("<specificskill><name>Perception</name><bonus>1</bonus></specificskill>"
                + "<specificskill><name>Perception</name><bonus>1</bonus><futurecondition>unknown</futurecondition></specificskill>");
            Require(duplicatePartial.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Text deduplication must not discard an unknown condition on the second effect.");
            string conditionalLifestyle = Describe("<lifestylecost lifestyle='Low' condition='once'>10</lifestylecost>");
            Require(conditionalLifestyle.Contains("Lifestyle: Low") && conditionalLifestyle.Contains("When: One-time cost"),
                "Lifestyle scope and one-time conditions must remain visible.");
            string expression = string.Join(" ", CreationQualityInfo.Effects(
                "<quality><bonus><specificskill><name>Test skill</name><bonus>Rating + 1</bonus><condition>Only at night</condition></specificskill></bonus></quality>"));
            Require(expression.Contains("Rating + 1") && expression.Contains("Only at night"),
                "Read-only help must preserve expressions and conditions without evaluating rules.");
            string unknown = string.Join(" ", CreationQualityInfo.Effects(
                "<quality><name>Catlike</name><id>00000000-0000-0000-0000-000000000001</id><bonus><unrecognized /></bonus></quality>"));
            Require(unknown == CreationFlowStrings.Get("Qualities.Info.Manual", ""),
                "An unknown source identity must not borrow another quality's prose from its display name.");
            try
            {
                CreationQualityInfo.Effects("<!DOCTYPE quality [<!ENTITY x 'unsafe'>]><quality>&x;</quality>");
                throw new InvalidOperationException("Quality help accepted a DTD.");
            }
            catch (System.Xml.XmlException) { }
        }
        finally { CultureInfo.CurrentUICulture = oldCulture; }
    }

    internal static async Task RunCreationQualityDetailsAsync(string contentRoot, string? smokeWorkspacePath = null)
    {
        VerifyQualitySummaryContent(contentRoot);
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var owners = new ControlledLinkedOwner();
            await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                creationFinalization: true, productionCreationOverview: true);
            var before = PrepareActualFinalizationReadyContext(runtime, stopBeforeQualities: true);
            if (smokeWorkspacePath is not null)
            {
                Require(Path.IsPathFullyQualified(smokeWorkspacePath), "Use an explicit private smoke fixture path.");
                // Preserve unmodified real Core output for the isolated AVD's
                // affected-route smoke, not a claim of UI-driven earlier steps.
                File.Copy(Path.Combine(runtime.StateDirectory, "workspaces", runtime.Id.Value + ".json"),
                    smokeWorkspacePath, overwrite: false);
                Console.WriteLine("QUALITY_SMOKE_WORKSPACE " + runtime.Id.Value);
            }
            await HydrateFinalizationOwnerAsync(runtime, owners, before);
            var coordinator = runtime.Coordinator;
            var original = coordinator.State;
            var loaded = await coordinator.LoadCreationQualitiesForDisplayAsync(original, default);
            var state = loaded.Value ?? throw new InvalidOperationException("SETUP: quality authority missing.");
            var catalogPage = new CreationQualitiesPage(coordinator);
            await MinimalPrepareAsync(catalogPage);
            var catalogNavigation = new NavigationPage(catalogPage);
            SearchBar Search() => MinimalVisible(catalogPage).OfType<SearchBar>().Single();
            var retainedSearch = Search();
            var retainedReview = MinimalVisible(catalogPage).OfType<Button>().Single(item =>
                item.AutomationId == "creation-qualities-open-review");
            var detachedHelp = MinimalVisible(catalogPage).OfType<Button>().First(item =>
                item.AutomationId?.StartsWith("creation-quality-info-", StringComparison.Ordinal) == true);
            retainedSearch.Text = "no-such-quality-focus-regression";
            ((ISearchBarController)retainedSearch).OnSearchButtonPressed();
            Require(ReferenceEquals(Search(), retainedSearch) && ReferenceEquals(retainedReview,
                MinimalVisible(catalogPage).OfType<Button>().Single(item => item.AutomationId == "creation-qualities-open-review")),
                "Submitting a Quality search must not detach its editor or rebuild unrelated actions.");
            Require(!MinimalVisible(catalogPage).OfType<Button>().Any(item =>
                item.AutomationId?.StartsWith("creation-quality-info-", StringComparison.Ordinal) == true),
                "An empty search retained old help actions.");
            ((IButtonController)detachedHelp).SendClicked();
            Require(ReferenceEquals(catalogNavigation.CurrentPage, catalogPage), "Filtered-out help navigated from a stale row.");
            retainedSearch.Text = string.Empty;
            Require(ReferenceEquals(Search(), retainedSearch) && MinimalVisible(catalogPage).OfType<Button>().Any(item =>
                item.AutomationId?.StartsWith("creation-quality-info-", StringComparison.Ordinal) == true),
                "Clearing a search detached the native editor instead of refreshing only results.");
            var nextPage = MinimalVisible(catalogPage).OfType<Button>().Single(item =>
                item.AutomationId == "creation-qualities-catalog-next");
            Require(nextPage.IsEnabled, "SETUP: expected multiple available catalog pages.");
            ((IButtonController)nextPage).SendClicked();
            string secondPage = MinimalVisibleText(catalogPage);
            ((IButtonController)nextPage).SendClicked();
            Require(ReferenceEquals(Search(), retainedSearch) && MinimalVisibleText(catalogPage) == secondPage,
                "Paging detached the editor or a detached pager changed the new result list.");
            await VerifyQualityHelpScrollReturnAsync(ui, catalogPage, catalogNavigation);
            retainedSearch = Search();
            retainedSearch.Text = "Ambidextrous";
            ((ISearchBarController)retainedSearch).OnSearchButtonPressed();
            Require(ReferenceEquals(Search(), retainedSearch)
                && MinimalVisibleText(catalogPage).Contains("Ambidextrous")
                && ReferenceEquals(catalogNavigation.CurrentPage, catalogPage),
                "Replacing and submitting a Quality search must stay in the catalog, not open Review.");
            MinimalRender(catalogPage);
            string freshCatalog = MinimalVisibleText(catalogPage);
            retainedSearch.Text = string.Empty;
            ((ISearchBarController)retainedSearch).OnSearchButtonPressed();
            Require(!ReferenceEquals(Search(), retainedSearch) && MinimalVisibleText(catalogPage) == freshCatalog,
                "A detached SearchBar changed the new catalog.");
            RequireSameRewardDocument(before, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
            var editor = CreationQualitiesPhoneAuthority.ProjectEditor(state, original);
            var draft = new CreationQualitiesPhoneDraft();
            draft.Bind(state, original);
            var option = draft.AvailableOptions(state, original, editor, default)
                .Where(item => !item.IsMetagenic)
                .OrderByDescending(item => !string.IsNullOrWhiteSpace(item.FollowUpChoiceLabel)).First();
            var configure = new CreationQualityConfigurePage(coordinator, state, editor, option, draft, original);
            var previewResult = coordinator.PreviewCreationQualities(state.Binding, [option.OptionId], original);
            Require(draft.TryAdopt(state, original, previewResult, [option.OptionId])
                && previewResult.Value is { CanConfirm: true }, "SETUP: selected quality must have a real confirmable preview.");
            var preview = previewResult.Value!;
            var checkpoint = CharacterCreationQualitiesCheckpoint.CreateReviewed(preview, [option.OptionId], Guid.NewGuid());
            var store = CharacterCreationQualitiesCheckpointStore.CreateDefault(original.DisplayOwnerContext,
                coordinator.IsCreationQualitiesOwnerCurrent);
            Require(store.TryCreate(checkpoint, out checkpoint, out string blocker), blocker);
            var review = new CreationQualitiesReviewPage(coordinator, checkpoint, store, original);
            var oldCulture = CultureInfo.CurrentUICulture;
            try
            {
                foreach (string locale in new[] { "en-GB", "de-AT", "es-MX" })
                {
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
                    VerifyQualityDisclosure(configure, "creation-quality-configure-technical-details",
                        "creation-quality-configure-toggle", option.OptionId, option.SourceId.ToString("D"));
                    string configureText = MinimalVisibleText(configure);
                    Require(configureText.Contains(option.Name) && configureText.Contains(CreationQualitiesPage.Signed(option.KarmaCost))
                        && (string.IsNullOrWhiteSpace(option.FollowUpChoiceLabel) || configureText.Contains(option.FollowUpChoiceLabel)),
                        "Configure lost the quality name, exact cost or readable follow-up.");
                    VerifyQualityDisclosure(review, "creation-qualities-review-technical-details",
                        "creation-qualities-confirm-draft", checkpoint.TransactionId.ToString("D"), preview.PreviewDigest,
                        preview.AuthorityDigest, preview.Binding.RawCharacterXmlDigest, preview.Binding.AuxiliaryStateDigest,
                        option.OptionId, option.SourceId.ToString("D"));
                    Require(MinimalVisibleText(review).Contains(option.Name)
                        && MinimalVisible(review).OfType<Button>().Single(button => button.AutomationId == "creation-qualities-confirm-draft").Text
                            == CreationFlowStrings.Get("Qualities.Review.Confirm", "missing"),
                        "Review lost the selected name or localized save action.");
                    var emptyPreview = coordinator.PreviewCreationQualities(state.Binding, [], original).Value!;
                    var empty = new CreationQualitiesReviewPage(coordinator,
                        CharacterCreationQualitiesCheckpoint.CreateReviewed(emptyPreview, [], Guid.NewGuid()), store, original);
                    MinimalRender(empty);
                    Require(MinimalVisibleText(empty).Contains(CreationFlowStrings.Get("Qualities.Review.Empty", "missing"))
                        && MinimalVisible(empty).OfType<Button>().Single(button => button.AutomationId == "creation-qualities-confirm-draft").IsEnabled,
                        "An empty valid review must explain that no additional qualities are selected.");
                    MinimalRequireNoMachineValues(empty);
                    foreach (string name in new[] { "Analytical Mind", "Catlike", "Unsteady Hands", "Aptitude", "Erased", "Animal Empathy" })
                    {
                        var helpOption = state.Authority.Options.First(item => item.Name == name);
                        var help = new CreationQualityInfoPage(coordinator, original, helpOption);
                        string helpText = MinimalVisibleText(help);
                        string expected = CreationFlowStrings.Get("Qualities.Summary." + helpOption.SourceId.ToString("D"), "");
                        Require(expected.Length > 40 && helpText.Contains(expected)
                            && !helpText.Contains("Rulebook", StringComparison.OrdinalIgnoreCase)
                            && !helpText.Contains("Regelbuch", StringComparison.OrdinalIgnoreCase)
                            && !helpText.Contains("página", StringComparison.OrdinalIgnoreCase),
                            "Quality help must contain the localized inline explanation, not a book citation.");
                        MinimalRequireNoMachineValues(help);
                    }
                    var ratedOption = state.Authority.Options.First(item => item.Name == "Will to Live" && item.Rating == 3);
                    string levelNotice = CreationFlowStrings.Format("Qualities.Info.BaseEffects", "missing", ratedOption.Rating);
                    var ratedHelp = new CreationQualityInfoPage(coordinator, original, ratedOption);
                    Require(MinimalVisibleText(ratedHelp).Contains(levelNotice),
                        "The catalog help page must pass the accepted option's level to the description.");
                    // Presentation-only grant fixture: no grant is persisted or
                    // admitted. Both help constructors must preserve the level.
                    var grant = new CharacterCreationGrantedQuality("help-grant-fixture", ratedOption.SourceId,
                        "help-selection-fixture", ratedOption.Name, ratedOption.Type, ratedOption.Rating, 0,
                        false, false, false, "Earlier choice", [], "help-digest-fixture");
                    var grantedHelp = new CreationQualityInfoPage(coordinator, original, grant, ratedOption.SourceNodeXml);
                    Require(MinimalVisibleText(grantedHelp).Contains(levelNotice),
                        "Already-granted qualities must retain the same base-versus-selected-level distinction.");
                    var firstLevelHelp = new CreationQualityInfoPage(coordinator, original, ratedOption with { Rating = 1 });
                    Require(!MinimalVisibleText(firstLevelHelp).Contains(CreationFlowStrings.Format("Qualities.Info.BaseEffects", "missing", 1)),
                        "A single-level description must not show an irrelevant combined-total notice.");
                }
                RequireSameRewardDocument(before, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
                Require(store.TryRead(out var unchanged, out blocker) && unchanged.CheckpointDigest == checkpoint.CheckpointDigest,
                    "Rendering/disclosure mutated the durable review.");

                Require(store.TryBeginApply(CharacterCreationQualitiesCheckpointCas.From(checkpoint), out var applying, out blocker), blocker);
                var result = await coordinator.ConfirmCreationQualitiesAsync(applying, display: original);
                Require(result is { Receipt: not null, MutationOutcomeKnown: true, Outcome: CreationQualitiesPhoneOutcomes.Applied },
                    "Actual quality confirmation failed.");
                Require(store.TryRecordApplied(CharacterCreationQualitiesCheckpointCas.From(applying), result.Receipt!, out var applied, out blocker), blocker);
                var saved = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
                Require(saved.ContentRevision == before.ContentRevision + 1 && saved.SavedRevision == saved.ContentRevision
                    && saved.Document.Content == before.Document.Content, "Quality confirmation must save one auxiliary revision, not live character effects.");
                await HydrateFinalizationOwnerAsync(runtime, owners, saved);
                var cold = coordinator.LoadCreationQualities().Value!;
                Require(CreationQualitiesPhoneAuthority.ReceiptMatchesPersistedState(applying, result.Receipt!, cold),
                    "Exact quality selection/receipt did not survive cold-store reopen.");
                var receipt = new CreationQualitiesReceiptPage(coordinator, applied, result.Receipt!, store);
                foreach (string locale in new[] { "en-GB", "de-AT", "es-MX" })
                {
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
                    VerifyQualityDisclosure(receipt, "creation-qualities-receipt-technical-details",
                        "creation-qualities-receipt-acknowledge", result.Receipt!.TransactionId.ToString("D"),
                        result.Receipt.ReceiptDigest, result.Receipt.DraftDigest, result.Receipt.PlanDigest, result.Receipt.CommandDigest);
                    Require(MinimalVisibleText(receipt).Contains(CreationFlowStrings.Get("Qualities.Receipt.Safe", "missing"))
                        && MinimalVisible(receipt).OfType<Button>().Single(button => button.AutomationId == "creation-qualities-receipt-acknowledge").Text
                            == CreationFlowStrings.Get("Qualities.Receipt.Continue", "missing"),
                        "Saved quality confirmation lost localized continuation or pending-finalization guidance.");
                }
                RequireSameRewardDocument(saved, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
                Require(store.TryRead(out var retained, out blocker) && retained.CheckpointDigest == applied.CheckpointDigest,
                    "Receipt disclosure acknowledged or changed the pending receipt.");
                Require(store.TryAcknowledgeApplied(CharacterCreationQualitiesCheckpointCas.From(applied), out blocker), blocker);
                var owner = owners.Current;
                var freshCatalogPage = new CreationQualitiesPage(coordinator);
                await MinimalPrepareAsync(freshCatalogPage);
                await VerifyQualityHelpScrollReturnAsync(ui, freshCatalogPage, new NavigationPage(freshCatalogPage), () =>
                {
                    owners.Set(ContactsOwnerB);
                    owners.Set(owner);
                });
                string staleCatalog = MinimalVisibleText(catalogPage);
                var staleSearch = Search();
                staleSearch.Text = "Catlike";
                ((ISearchBarController)staleSearch).OnSearchButtonPressed();
                Require(MinimalVisibleText(catalogPage) == staleCatalog,
                    "A search callback admitted catalog work across an owner A→B→A transition.");
                foreach (NativePageBase stale in new NativePageBase[] { configure, review, receipt })
                {
                    MinimalRender(stale);
                    Require(!MinimalVisible(stale).OfType<Button>().Any(), "Stale owner generation still exposes quality actions or diagnostics.");
                    MinimalRequireNoMachineValues(stale);
                }
                RequireSameRewardDocument(saved, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
            }
            finally { CultureInfo.CurrentUICulture = oldCulture; }
            Console.WriteLine("PASS quality configure/review/receipt: EN/DE/ES, exact hidden diagnostics, stale controls, one save/cold reopen, no display mutations");
        });
    }

    private static async Task VerifyQualityHelpScrollReturnAsync(
        IssuedPageUiContext ui, CreationQualitiesPage page, NavigationPage navigation, Action? invalidateBeforeDispatch = null)
    {
        const double savedY = 735;
        var scroll = (ScrollView)page.Content!;
        var controller = (IScrollViewController)scroll;
        var observed = new List<double>();
        controller.ScrollToRequested += (_, request) =>
        {
            observed.Add(request.ScrollY);
            controller.SendScrollFinished();
        };
        string Range() => MinimalVisible(page).OfType<Label>().Single(item =>
            item.AutomationId == "creation-qualities-catalog-range").Text;
        string range = Range();
        controller.SetScrolledPosition(0, savedY);
        var helpButton = MinimalVisible(page).OfType<Button>().First(item =>
            item.AutomationId?.StartsWith("creation-quality-info-", StringComparison.Ordinal) == true);
        await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)helpButton).SendClicked()));
        var help = navigation.CurrentPage;
        Require(help is CreationQualityInfoPage, "Help did not open from the current catalog.");
        await navigation.PopAsync();
        controller.SetScrolledPosition(0, 0);
        // Exercise the real fresh appearance load, not cached authority. Headless
        // MAUI has no native layout/attachment callbacks, supplied explicitly here.
        await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing")));
        ((IView)scroll).Arrange(new Microsoft.Maui.Graphics.Rect(0, 0, 400, 600));
        typeof(ScrollView).GetProperty(nameof(ScrollView.ContentSize))!.SetValue(scroll,
            new Microsoft.Maui.Graphics.Size(400, 3000));
        typeof(CreationQualitiesPage).GetMethod("RestoreAfterHelp", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(page, [help]);
        invalidateBeforeDispatch?.Invoke();
        await ui.DrainDispatchedAsyncVoidAsync();
        if (invalidateBeforeDispatch is not null)
        {
            Require(observed.Count == 0, "A queued help restoration scrolled after an owner A→B→A transition.");
            IssuedPageLifecycle(page, "OnDisappearing");
            return;
        }
        Require(observed.SequenceEqual([savedY]) && Range() == range,
            "Returning from read-only help must restore the position and catalog page after fresh loading.");
        MinimalRender(page);
        await ui.DrainDispatchedAsyncVoidAsync();
        Require(observed.Count == 1, "A later catalog render replayed the help scroll restoration.");
        IssuedPageLifecycle(page, "OnDisappearing");
    }

    private static void VerifyQualityDisclosure(NativePageBase page, string panelId, string actionId, params string[] exactValues)
    {
        MinimalRender(page);
        MinimalRequireNoMachineValues(page);
        Require(!MinimalVisibleText(page).Contains("CharacterDocumentChanged"), "Ordinary guidance leaks implementation jargon.");
        var body = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
        var panel = body.Children.OfType<VerticalStackLayout>().Single(item => item.AutomationId == panelId);
        var toggle = panel.Children.OfType<Button>().Single();
        var action = MinimalVisible(page).OfType<Button>().Single(item => item.AutomationId == actionId);
        Require(action.IsEnabled && body.Children.IndexOf(action) < body.Children.IndexOf(panel)
            && toggle.Text == CreationFlowStrings.Get("Qualities.ShowDetails", "missing"),
            "Technical details displaced or disabled the primary action.");
        ((IButtonController)toggle).SendClicked();
        Require(exactValues.All(value => MinimalVisibleText(page).Contains(value, StringComparison.Ordinal))
            && toggle.Text == CreationFlowStrings.Get("Qualities.HideDetails", "missing"),
            "Expanded quality diagnostics lost exact values or localization.");
        ((IButtonController)toggle).SendClicked();
        MinimalRequireNoMachineValues(page);
        MinimalRender(page);
        ((IButtonController)toggle).SendClicked();
        MinimalRequireNoMachineValues(page);
        Require(!((VisualElement)panel.Children[1]).IsVisible,
            "A detached quality disclosure reopened its old data after refresh.");
    }

    internal static async Task RunMinimalUiAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            foreach (var (locale, saveGear, saveBudget) in new[]
            {
                ("en-GB", "Save equipment", "Save budget"),
                ("de-AT", "Ausrüstung speichern", "Budget speichern"),
                ("es-MX", "Guardar equipo", "Guardar presupuesto")
            })
            {
                var localized = AndroidSurfaceStrings.Resolve(locale);
                Require(localized["GearPreview.Confirm"] == saveGear
                    && localized["ResourcesPreview.Confirm"] == saveBudget,
                    "Creation confirmation must describe the action in the player's language.");
                foreach (var key in new[] { "Resources.CurrentBudget", "Resources.CoreAuthority",
                    "Gear.DraftBasket", "Gear.ActiveCatalog", "GearPreview.ExactProjection" })
                    Require(!Regex.IsMatch(localized[key], "Core|XML|authority|autoridad|Autorität",
                        RegexOptions.IgnoreCase), "Player-facing headings expose implementation jargon.");
            }
            const string id = "c49a893a-d445-4aac-bec0-c8501cba4c2c";
            var label = NativeTheme.Body(id);
            var details = NativeTheme.TechnicalDetails(label, "test-diagnostics");
            var root = new VerticalStackLayout { details };
            var toggle = details.Children.OfType<Button>().Single();
            Require(!label.IsVisible && !MinimalVisibleText(root).Contains(id),
                "Diagnostics are exposed by default.");
            ((IButtonController)toggle).SendClicked();
            Require(label.IsVisible && label.Text == id && MinimalVisibleText(root).Contains(id),
                "Explicit troubleshooting lost the exact value.");
            ((IButtonController)toggle).SendClicked();
            Require(!label.IsVisible, "Diagnostics cannot be collapsed.");
            root.Clear();
            ((IButtonController)toggle).SendClicked();
            Require(!label.IsVisible, "A detached disclosure reopened old diagnostics.");
            Require(NativeTheme.Body(id).Text == id && NativeTheme.BookProse(id).Text == id,
                "Minimalism must not rewrite arbitrary player text or prose.");
            Require(NativeTheme.Body("Readable").FontSize >= 15
                && NativeTheme.BookProse("Chapter").FontSize >= 18
                && NativeTheme.PrimaryButton("Continue").HeightRequest >= 48,
                "Simplification shrank readable text or touch targets.");
            var overlay = NativeAuthoritySemantics.Overlay(NativeTheme.Body("Saved"),
                NativeAuthoritySemantics.Identifier("test-machine-id", id));
            Require(!MinimalVisibleText(overlay).Contains(id),
                "Ordinary-build TalkBack exposes invisible machine values.");

            var owners = new ControlledLinkedOwner();
            await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                creationFinalization: true, productionCreationOverview: true, creationPrerequisite: true);
            var before = PrepareActualFinalizationReadyContext(runtime);
            await HydrateFinalizationOwnerAsync(runtime, owners, before);
            AssertCreationReadinessCopy(runtime.Coordinator);
            var currentCulture = CultureInfo.CurrentUICulture;
            try
            {
                foreach (var (locale, words) in new[] { ("en-GB", "saved Attributes"),
                             ("de-AT", "gespeicherten Attribute"), ("es-MX", "Atributos guardados") })
                {
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
                    const string locked = "creation-prerequisite-dependent-attributes-draft-exists";
                    string message = CreationFlowStrings.DashboardBlocker(locked);
                    Require(message.Contains(words) && message == CreationFlowStrings.Get("Dashboard.MethodLocked", "missing"),
                        "Locked method lacks localized dependent-Attributes guidance.");
                    Require(CreationFlowStrings.DashboardBlocker("fixture-unknown-9d74b5ce")
                        == CreationFlowStrings.Get("Dashboard.StepBlocked", "missing"),
                        "Unknown codes must stay in diagnostics rather than leak into ordinary guidance.");
                }
            }
            finally { CultureInfo.CurrentUICulture = currentCulture; }
            var lockedMethod = await runtime.Coordinator.LoadCreationPrerequisiteAsync();
            Require(lockedMethod.Blockers.Concat(lockedMethod.Value?.Blockers ?? []).Contains(
                "creation-prerequisite-dependent-attributes-draft-exists"),
                "SETUP: saved Attributes must actually lock prerequisite edits.");
            var dashboard = new BuildPage(runtime.Coordinator);
            var snapshot = runtime.Coordinator.State.CreationWizard!;
            Require(CreationDashboardProjectionBinding.TryCreate(runtime.Coordinator.State, snapshot, out var binding),
                "SETUP: current dashboard binding missing.");
            var projection = CreationDashboardAuthorityProjection.Loading(binding!) with
            {
                Prerequisite = lockedMethod,
                Progress = CreationDashboardAuthorityPhaseProgress.ForBuildMethod(snapshot.BuildMethod) with
                { Prerequisite = CreationDashboardAuthorityPhaseState.Ready }
            };
            var methodRoute = (CreationBudgetRoute)typeof(BuildPage).GetMethod("AddCreationMethodRoute",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dashboard, [snapshot, projection, lockedMethod])!;
            Require(!methodRoute.CanOpen && methodRoute.Detail == CreationFlowStrings.DashboardBlocker(
                "creation-prerequisite-dependent-attributes-draft-exists")
                && methodRoute.Blockers.SequenceEqual(["creation-prerequisite-dependent-attributes-draft-exists"])
                && !MinimalVisible(dashboard).OfType<Button>().Single(x =>
                    x.AutomationId == "creation-stage-method").IsEnabled,
                "Plain locked-method guidance changed readiness or gave a misleading Karma reason.");
            var header = new VerticalStackLayout();
            bool hasPicker = (bool)typeof(BuildPage).GetMethod("AddWorkspacePicker",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dashboard, [header])!;
            Require(hasPicker == (runtime.Coordinator.State.OpenWorkspaces.Count > 1)
                && (!hasPicker || header.Children.OfType<Picker>().Single().AutomationId == "build-workspace-picker"),
                "Dashboard heading must retain the workspace selector only when needed.");
            var actual = new CharacterCreationGearInteractionPresenter(
                runtime.Services.GetRequiredService<ICharacterCreationGearService>(),
                runtime.Services.GetRequiredService<IOwnerBoundCharacterCreationGearService>());
            // Priority editing is intentionally locked once dependent stages
            // exist. Use a real new runner, not the ready-for-Gear fixture.
            await using var priorityRuntime = new NativeRewardRuntime(contentRoot,
                linkedOwners: owners, creationPrerequisite: true, creationAttributes: true,
                productionCreationOverview: true);
            await priorityRuntime.Coordinator.InitializeAsync();
            await AccountStartupTask(priorityRuntime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
            var prioritySeed = PreparePrerequisiteOwnerFixture(priorityRuntime);
            await HydrateFinalizationOwnerAsync(priorityRuntime, owners, prioritySeed);
            var prerequisite = (await priorityRuntime.Coordinator.LoadCreationPrerequisiteAsync()).Value
                ?? throw new InvalidOperationException("SETUP: real prerequisite authority unavailable.");
            var (assignments, selections) = PrerequisiteSelections(prerequisite);
            var assignmentsPreview = (await priorityRuntime.Coordinator.PreviewCreationPrerequisiteAsync(
                prerequisite.Binding, assignments, selections)).Value!;
            var assignmentReview = new CreationPrerequisitePreviewPage(priorityRuntime.Coordinator,
                assignmentsPreview, assignments, selections, CharacterCreationBuildMethods.Priority);
            await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(assignmentReview, "OnAppearing")));
            MinimalRequireNoMachineValues(assignmentReview);
            MinimalRequireFreshDisclosure(assignmentReview);
            Require(MinimalVisibleText(assignmentReview).Contains(assignmentsPreview.TalentSelection!.Name)
                && MinimalVisible(assignmentReview).OfType<Button>().Single(x =>
                    x.AutomationId == "creation-prerequisite-confirm").IsEnabled,
                "Readable assignments lost the actual Talent or exact confirmation.");
            ((IButtonController)MinimalVisible(assignmentReview).OfType<Button>().Single(x =>
                x.AutomationId == "creation-prerequisite-preview-details-toggle")).SendClicked();
            Require(MinimalVisibleText(assignmentReview).Contains(assignmentsPreview.PreviewDigest),
                "Review diagnostics must retain the exact preview digest.");
            IssuedPageLifecycle(assignmentReview, "OnDisappearing");
            var priorities = new CreationPrerequisitePage(priorityRuntime.Coordinator, prerequisite);
            MinimalRender(priorities);
            MinimalRequireNoMachineValues(priorities);
            Require(MinimalVisible(priorities).OfType<Button>().Any(x =>
                    x.AutomationId == "creation-prerequisite-prepare-preview"),
                "Minimal Priorities hid its review action.");
            var draft = new CreationPrerequisitePhoneDraft();
            draft.Bind(prerequisite, priorityRuntime.Coordinator.State);
            var category = new CreationPriorityCategoryPage(priorityRuntime.Coordinator, draft, prerequisite,
                CharacterCreationPriorityCategoryIds.Attributes);
            MinimalRender(category);
            MinimalRequireNoMachineValues(category);
            Require(MinimalVisible(category).OfType<Button>().Count(x =>
                    x.AutomationId?.StartsWith("creation-prerequisite-rank-") == true) == 5,
                "Minimal rank list removed choices or their unavailability explanations.");
            Require(draft.TrySelect(prerequisite, priorityRuntime.Coordinator.State,
                CharacterCreationPriorityCategoryIds.Heritage, "A"), "SETUP: Heritage rank unavailable.");
            var heritage = new CreationPriorityDetailPage(priorityRuntime.Coordinator, draft, prerequisite,
                CharacterCreationPriorityCategoryIds.Heritage);
            MinimalRender(heritage);
            MinimalRequireNoMachineValues(heritage);
            Require(MinimalVisible(heritage).OfType<Button>().Any(x =>
                x.AutomationId?.StartsWith("creation-prerequisite-heritage-option-") == true),
                "Minimal Heritage list has no actual choices.");
            Require(draft.TrySelect(prerequisite, priorityRuntime.Coordinator.State,
                CharacterCreationPriorityCategoryIds.Talent, "B"), "SETUP: Talent rank unavailable.");
            var talent = new CreationPriorityDetailPage(priorityRuntime.Coordinator, draft, prerequisite,
                CharacterCreationPriorityCategoryIds.Talent);
            MinimalRender(talent);
            MinimalRequireNoMachineValues(talent);
            Require(MinimalVisible(talent).OfType<Button>().Any(x =>
                x.AutomationId?.StartsWith("creation-prerequisite-talent-option-") == true),
                "Minimal Talent list has no actual choices.");
            foreach (var grantedTalent in draft.TalentOptions(prerequisite, priorityRuntime.Coordinator.State)
                .Where(option => option.IsEnabled && option.Blockers.Count == 0
                    && (option.ActiveSkillGrant is not null || option.SkillGroupGrant is not null)
                    && CreationPrerequisitePhoneAuthority.IsTalentGrantAuthoritySupported(option)))
            {
                Require(draft.TrySelectTalent(prerequisite, priorityRuntime.Coordinator.State, grantedTalent.SelectionId),
                    "SETUP: supported granted-skill Talent cannot be selected.");
                var grantPage = new CreationTalentSkillGrantPage(priorityRuntime.Coordinator, draft, prerequisite, grantedTalent.SelectionId);
                MinimalRender(grantPage);
                MinimalRequireNoMachineValues(grantPage);
                MinimalRequireFreshDisclosure(grantPage);
                Require(MinimalVisibleText(grantPage).Contains(grantedTalent.Name)
                    && MinimalVisibleText(grantPage).Contains("Granted rating"),
                    "Talent skill choices lost the readable Talent or actual granted rating.");
                ((IButtonController)MinimalVisible(grantPage).OfType<Button>().Single(button =>
                    button.AutomationId == "creation-prerequisite-talent-grant-details-toggle")).SendClicked();
                Require(MinimalVisibleText(grantPage).Contains(grantedTalent.ActiveSkillGrant?.GrantDigest
                    ?? grantedTalent.SkillGroupGrant!.GrantDigest), "Talent diagnostics lost the exact grant digest.");
            }
            var confirmedAssignments = await priorityRuntime.Coordinator.ConfirmCreationPrerequisiteAsync(
                assignmentsPreview, assignments, selections);
            Require(confirmedAssignments.Outcome == CharacterCreationFoundationOutcomes.Success,
                "SETUP: prerequisite confirmation failed.");
            var attributesAuthority = priorityRuntime.Coordinator.LoadCreationAttributes().Value
                ?? throw new InvalidOperationException("SETUP: Attribute authority unavailable.");
            Require(CreationAttributesPhoneAuthority.IsReady(
                attributesAuthority, priorityRuntime.Coordinator.State), "SETUP: editable Attribute authority unavailable.");
            var attributesPage = new CreationAttributesPage(priorityRuntime.Coordinator, attributesAuthority);
            await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(attributesPage, "OnAppearing")));
            var scroll = (ScrollView)attributesPage.Content!;
            Element? scrollTarget = null;
            ((IScrollViewController)scroll).ScrollToRequested += (_, request) =>
            {
                scrollTarget = request.Element;
                ((IScrollViewController)scroll).SendScrollFinished();
            };
            var jump = MinimalVisible(attributesPage).OfType<Button>().Single(x =>
                x.AutomationId == "creation-attributes-budget-normal-jump");
            ((IButtonController)jump).SendClicked();
            Require(scrollTarget?.AutomationId == "creation-attributes-normal-heading",
                "Normal points must jump to the actual normal Attribute list.");
            foreach (var attribute in attributesAuthority.Attributes)
            {
                var value = MinimalVisible(attributesPage).OfType<Label>().Single(x =>
                    x.AutomationId == "creation-attributes-open-" + CreationAttributesPage.Token(attribute.AttributeId) + "-value");
                Require(value.Text == attribute.Current.ToString(CultureInfo.InvariantCulture)
                    && value.FontAttributes.HasFlag(FontAttributes.Bold) && value.FontSize >= 24
                    && value.TextColor == NativeTheme.Text, "Current Attribute rating is not prominent/readable.");
            }
            scrollTarget = null;
            MinimalRender(attributesPage);
            ((IButtonController)jump).SendClicked();
            Require(scrollTarget is null, "A detached budget control still scrolls a refreshed screen.");
            MinimalRequireNoMachineValues(attributesPage);
            MinimalRequireFreshDisclosure(attributesPage);
            IssuedPageLifecycle(attributesPage, "OnDisappearing");
            var gear = new CreationGearPage(runtime.Coordinator, actual, runtime.Presenter);
            await MinimalPrepareAsync(gear);
            var visible = MinimalVisibleText(gear);
            MinimalRequireNoMachineValues(gear);
            Require(MinimalVisible(gear).OfType<Button>().Any(x => x.AutomationId == "creation-gear-preview")
                && MinimalVisible(gear).OfType<SearchBar>().Any()
                && visible.Contains("¥"), "Minimal Gear hid budget, search or review.");
            var copy = AndroidSurfaceStrings.Resolve();
            var unchanged = MinimalVisible(gear).OfType<Label>()
                .Single(x => x.AutomationId == "creation-gear-preview-authority");
            Require(unchanged.Text == copy["Gear.ChangeBasket"]
                && unchanged.TextColor.Equals(NativeTheme.Muted)
                && !MinimalVisible(gear).OfType<Button>()
                    .Single(x => x.AutomationId == "creation-gear-preview").IsEnabled,
                "An unchanged saved basket is neutral guidance, not an error or a new save.");
            var disclosure = MinimalVisible(gear).OfType<Button>()
                .Single(x => x.AutomationId == "creation-gear-details-toggle");
            ((IButtonController)disclosure).SendClicked();
            Require(MinimalVisible(gear).OfType<Label>().Any(x =>
                    x.AutomationId == "creation-gear-binding-snapshot-digest"),
                "Gear diagnostic anchors were deleted instead of disclosed.");
            // A refresh must not retain an expanded old snapshot.
            MinimalRender(gear);
            MinimalRequireNoMachineValues(gear);

            var original = runtime.Coordinator.State;
            var prepared = actual.Prepare(original, [new("gear:" + id, 1)]).PreparedPreview
                ?? throw new InvalidOperationException("SETUP: actual Gear preview unavailable.");
            var preview = new CreationGearPreviewPage(runtime.Coordinator, actual, runtime.Presenter,
                prepared, AndroidSurfaceStrings.Resolve(), original);
            await MinimalPrepareAsync(preview);
            MinimalRequireNoMachineValues(preview);
            Require(MinimalVisible(preview).OfType<Button>().Any(x =>
                    x.AutomationId == "creation-gear-confirm" && x.IsEnabled
                    && x.Text == copy["GearPreview.Confirm"]),
                "Minimal preview hid or disabled explicit confirmation.");

            var resources = new CharacterCreationResourcesInteractionPresenter(
                runtime.Services.GetRequiredService<ICharacterCreationResourcesService>(),
                runtime.Services.GetRequiredService<IOwnerBoundCharacterCreationResourcesService>());
            var resourcePage = new CreationResourcesPage(runtime.Coordinator, resources, runtime.Presenter, actual);
            await MinimalPrepareAsync(resourcePage);
            MinimalRequireNoMachineValues(resourcePage);
            var resourceText = MinimalVisibleText(resourcePage);
            Require(resourceText.Contains("¥")
                && !resourceText.Contains(copy["Common.DraftRevision"])
                && !resourceText.Contains(copy["Resources.ExactBudget"])
                && MinimalVisible(resourcePage).OfType<Button>().Any(x =>
                    x.AutomationId == "creation-resources-open-gear" && x.IsEnabled),
                "Resources must retain budget and next action without repeating technical status.");
            // Exercise the warning branch without modifying the workspace or
            // pretending a fabricated budget is admissible for a save.
            var budget = resources.Load(original).State!.Budget;
            typeof(CreationResourcesPage).GetMethod("AddBudget", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(resourcePage, [budget with { IsExact = false }, "Warning test", "test-incomplete-budget"]);
            Require(MinimalVisible(resourcePage).OfType<Label>().Any(x =>
                x.Text == copy["Resources.IncompleteBudget"] && x.TextColor.Equals(NativeTheme.Danger)),
                "Minimal Resources suppressed an incomplete-cost warning.");
            Require(FinalizationDocumentDigest(new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!)
                    == FinalizationDocumentDigest(before),
                "Rendering/disclosing minimal UI mutated the workspace.");
        });
        Console.WriteLine("PASS minimal native UI: localized plain actions, collapsed diagnostics, neutral unchanged basket, retained budget/warnings/confirm, unchanged prose and persistence");
    }

    private static async Task MinimalPrepareAsync(NativePageBase page)
    {
        await (Task)page.GetType().GetMethod("PrepareForAppearanceRefreshAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [CancellationToken.None])!;
        MinimalRender(page);
    }

    private static void MinimalRequireFreshDisclosure(NativePageBase page)
    {
        var field = page.GetType().GetField("_technicalDetails", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var previous = (View)field.GetValue(page)!;
        var parent = previous.Parent;
        MinimalRender(page);
        var current = (View)field.GetValue(page)!;
        Require(!ReferenceEquals(previous, current) && parent is not null
            && ReferenceEquals(previous.Parent, parent) && current.Parent is not null
            && !ReferenceEquals(current.Parent, parent),
            "A refreshed disclosure reused a native child still owned by its previous container.");
    }

    private static void MinimalRender(NativePageBase page) => page.GetType()
        .GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);

    private static IEnumerable<VisualElement> MinimalVisible(IVisualTreeElement element)
    {
        if (element is VisualElement { IsVisible: false }) yield break;
        if (element is VisualElement view) yield return view;
        foreach (var child in element.GetVisualChildren())
            foreach (var descendant in MinimalVisible(child))
                yield return descendant;
    }

    private static string MinimalVisibleText(IVisualTreeElement element) => string.Join("\n",
        MinimalVisible(element).SelectMany(view => new[]
        {
            view is Label label ? label.Text : view is Button button ? button.Text : string.Empty,
            SemanticProperties.GetDescription(view)
        }));

    private static void MinimalRequireNoMachineValues(IVisualTreeElement element)
    {
        Require(!Regex.IsMatch(MinimalVisibleText(element),
            @"sha256:|\b(?:[0-9a-f]{32}|[0-9a-f]{64})\b|\b[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}\b",
            RegexOptions.IgnoreCase), "Normal UI/accessibility contains machine identifiers.");
    }
}
