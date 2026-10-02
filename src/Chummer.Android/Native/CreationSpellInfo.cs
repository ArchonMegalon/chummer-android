using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>Display-only help from the accepted Core definition. Never admits a selection.</summary>
internal static class CreationSpellInfo
{
    public static CharacterCreationMagicResonanceCatalogOption? Resolve(
        CharacterCreationMagicResonanceState? state, CharacterCreationMagicResonanceOptionProjection option)
    {
        if (state is null || option.Identity.Kind != CharacterCreationMagicResonanceKinds.Spell) return null;
        var matches = state.Authority.Spells.Where(source => source.Identity == option.Identity
            && source.SourceNodeDigest == option.SourceNodeDigest).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    public static string Summary(CharacterCreationMagicResonanceCatalogOption? option)
    {
        if (option is null || option.Identity.Kind != CharacterCreationMagicResonanceKinds.Spell
            || option.CanonicalSourceXml.Length > 262144) return Unavailable();
        try
        {
            // Prohibit entities before invoking the ordinary typed-source validation.
            using var reader = XmlReader.Create(new StringReader(option.CanonicalSourceXml), new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 262144 });
            var source = XElement.Load(reader);
            if (!CharacterCreationMagicResonanceFinalizationRules.HasValidOptionPayload(option)) return Unavailable();
            var parts = new List<string>();
            string id = option.Identity.SourceId;
            string authored = Text("Summary." + id, string.Empty);
            string expected = CreationFlowStrings.Get("Spells.SummarySource." + id, string.Empty, CultureInfo.InvariantCulture);
            string actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(option.CanonicalSourceXml)));
            if (authored.Length > 0 && expected == actual) parts.Add(authored);
            else if (authored.Length > 0) parts.Add(Text("Changed", "Definition changed; showing this version's spell profile."));

            string Value(string key) => source.Element(key)?.Value.Trim() ?? string.Empty;
            if (parts.Count == 0)
                parts.Add(Text("Category." + Value("category"), Value("category")));
            if (Value("category") == "Combat")
            {
                var descriptors = Value("descriptor").Split(',', StringSplitOptions.TrimEntries);
                if (descriptors.Contains("Direct", StringComparer.Ordinal)) parts.Add(Text("Direct", "Direct magical attack."));
                if (descriptors.Contains("Indirect", StringComparer.Ordinal)) parts.Add(Text("Indirect", "Indirect magical attack."));
            }
            Add("Damage", Value("damage"));
            Add("Range", Value("range"));
            Add("Duration", Value("duration"));
            Add("Type", Value("type"));
            string drain = Value("dv");
            if (drain.Length > 0)
                parts.Add(CreationFlowStrings.Format("Spells.Drain", "Drain: {0} (F = Force).", drain));
            return string.Join(" ", parts.Where(part => part.Length > 0));

            void Add(string field, string value)
            {
                if (value.Length == 0) return;
                string translated = Text(field + "." + value, string.Empty);
                parts.Add(translated.Length > 0 ? translated
                    : CreationFlowStrings.Format("Spells.Raw." + field, field + ": {0}.", value));
            }
        }
        catch (Exception error) when (error is XmlException or InvalidOperationException or ArgumentException)
        { return Unavailable(); }
    }

    private static string Text(string key, string fallback) => CreationFlowStrings.Get("Spells." + key, fallback);
    private static string Unavailable() => Text("Unavailable", "The spell description could not be loaded. Reopen the magic choices.");
}
