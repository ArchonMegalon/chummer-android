using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

internal sealed record CreationQualityFilters(int Polarity = 0, int CostBand = 0, int Sort = 0)
{
    // Display-only magnitude, including Karma received for disadvantages.
    internal static long Cost(CharacterCreationQualitiesDesktopOption option) => Math.Abs((long)option.KarmaCost);

    internal bool Matches(CharacterCreationQualitiesDesktopOption option)
        => (Polarity == 0 || option.Type == (Polarity == 1
                ? CharacterCreationQualityType.Positive : CharacterCreationQualityType.Negative))
           && (CostBand switch
           {
               1 => Cost(option) == 0,
               2 => Cost(option) is >= 1 and <= 5,
               3 => Cost(option) is >= 6 and <= 10,
               4 => Cost(option) is >= 11 and <= 15,
               5 => Cost(option) is >= 16 and <= 20,
               6 => Cost(option) >= 21,
               _ => true
           });

    internal CharacterCreationQualitiesDesktopOption[] Apply(
        IEnumerable<CharacterCreationQualitiesDesktopOption> admitted,
        Func<CharacterCreationQualitiesDesktopOption, bool> search,
        Func<string, bool> selected,
        IReadOnlyDictionary<string, QualityFitHint> hints)
    {
        // Never evaluate eligibility here, or lose the user's removal controls.
        var rows = admitted.Where(option => selected(option.OptionId) || Matches(option) && search(option))
            .OrderByDescending(option => selected(option.OptionId));
        return (Sort switch
        {
            1 => rows.ThenBy(Cost),
            2 => rows.ThenByDescending(Cost),
            3 => rows.ThenByDescending(option => hints.GetValueOrDefault(option.OptionId)?.Score ?? 0),
            _ => rows
        }).ThenBy(option => QualityCatalogStrings.Name(option.SourceId, option.Name), StringComparer.CurrentCultureIgnoreCase)
          .ThenBy(option => option.OptionId, StringComparer.Ordinal).ToArray();
    }
}

internal sealed record QualityFitHint(int Score, string SkillName);

/// <summary>
/// Optional editorial relevance, never a rules/eligibility or optimal-build claim.
/// Only admitted source effects and current trained skills are compared. Unknown
/// effects remain neutral; disadvantages are not advertised as harmless Karma.
/// </summary>
internal static class CreationQualityFit
{
    internal static IReadOnlyDictionary<string, QualityFitHint> Project(
        IEnumerable<CharacterCreationQualityCatalogOption> options,
        IReadOnlyList<CharacterCreationSkillProjection> skills,
        CancellationToken cancellationToken)
    {
        var trained = skills.Where(skill => skill.Kind == CharacterCreationSkillKinds.Active
            && skill.IsEnabled && skill.Blockers.Count == 0 && skill.EffectiveRating > 0).ToArray();
        var result = new Dictionary<string, QualityFitHint>(StringComparer.Ordinal);
        if (trained.Length == 0) return result;
        // Multiple ratings/follow-ups share source bytes. Parse once per definition,
        // off the UI thread, rather than once per rendered row or filter click.
        var sources = new Dictionary<string, XElement?>(StringComparer.Ordinal);
        foreach (var option in options)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (option.Type != CharacterCreationQualityType.Positive) continue;
            if (!sources.TryGetValue(option.SourceNodeXml, out var source))
            {
                source = Read(option.SourceNodeXml);
                sources.Add(option.SourceNodeXml, source);
            }
            if (source is null) continue;
            var match = trained.Where(skill => HasSkillLink(source, option, skill))
                .OrderByDescending(skill => skill.EffectiveRating)
                .ThenBy(skill => skill.SourceSkillId, StringComparer.Ordinal).FirstOrDefault();
            if (match is not null)
                result[option.OptionId] = new(match.EffectiveRating!.Value,
                    SkillCatalogStrings.SkillName(match.Kind, match.SourceSkillId, match.Name));
        }
        return result;
    }

    private static XElement? Read(string xml)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 262144 });
            return XElement.Load(reader);
        }
        catch (XmlException) { return null; }
    }

    private static bool HasSkillLink(XElement source, CharacterCreationQualityCatalogOption option,
        CharacterCreationSkillProjection skill)
    {
        foreach (var effect in source.Element("bonus")?.Elements() ?? [])
        {
            bool positive = decimal.TryParse(effect.Element("bonus")?.Value,
                NumberStyles.Number, CultureInfo.InvariantCulture, out var bonus) && bonus > 0;
            if (positive && (effect.Name.LocalName switch
                {
                    "specificskill" => effect.Element("name")?.Value == skill.Name,
                    "skillcategory" => effect.Element("name")?.Value == skill.Category,
                    "skillattribute" => effect.Element("name")?.Value == skill.DefaultAttribute,
                    _ => false
                })) return true;
            if (effect.Name.LocalName == "selectskill" && option.FollowUpChoiceLabel == skill.Name
                && decimal.TryParse(effect.Element("max")?.Value, out var maximum) && maximum > 0) return true;
        }
        // A few useful non-numeric effects have editorial links. They apply only
        // to the exact definition already bound by the original-summary inventory,
        // never merely to a surviving ID after a custom-data amendment.
        string expected = CreationFlowStrings.Get("Qualities.SummarySource." + option.SourceId.ToString("D"),
            string.Empty, CultureInfo.InvariantCulture);
        if (expected.Length == 0 || !string.Equals(expected,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.ToString(SaveOptions.DisableFormatting)))),
            StringComparison.Ordinal)) return false;
        return option.SourceId.ToString("D") switch
        {
            "41cc3e26-ae55-4e28-bd6a-b08866c21424" or "52648a83-1d2a-4b04-b7fc-ee5a62bce4f7"
                => skill.Name is "Hacking" or "Cybercombat" or "Electronic Warfare" or "Computer",
            "5c18a150-8e38-4f6d-b4f5-b1b674a3581e" => skill.Category == "Vehicle Active",
            "4d5c22b9-4908-493d-a6f7-4ccc3066888c"
                => skill.Name is "Spellcasting" or "Ritual Spellcasting" or "Software",
            "b7866fb4-3747-4caf-9240-69cbdd79ce78" or "291efdb6-a8b8-49ce-b2be-72f9d3f8a243"
                => skill.Category == "Combat Active",
            _ => false
        };
    }
}
