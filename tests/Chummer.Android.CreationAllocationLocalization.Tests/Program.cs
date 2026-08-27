using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Chummer.Android.Native;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

string workspace = Directory.GetCurrentDirectory();
string native = Path.Combine(workspace, "src", "Chummer.Android", "Native");
string localization = Path.Combine(
    workspace,
    "src",
    "Chummer.Android",
    "Resources",
    "Localization");
Dictionary<string, string> neutral = ReadResource(
    Path.Combine(localization, "CreationAllocationStrings.resx"));
Dictionary<string, string> german = ReadResource(
    Path.Combine(localization, "CreationAllocationStrings.de.resx"));
Dictionary<string, string> spanish = ReadResource(
    Path.Combine(localization, "CreationAllocationStrings.es.resx"));

Assert(neutral.Count > 0, "neutral allocation catalog must not be empty");
AssertSameKeys(neutral, german, "de");
AssertSameKeys(neutral, spanish, "es");
AssertSamePlaceholders(neutral, german, "de");
AssertSamePlaceholders(neutral, spanish, "es");

Assert(
    CreationAllocationStrings.Get(
        "Attributes.Heading",
        "fallback",
        CultureInfo.GetCultureInfo("en-GB")) == "Allocate Attributes",
    "en-GB must use the neutral English catalog");
Assert(
    CreationAllocationStrings.Get(
        "Attributes.Heading",
        "fallback",
        CultureInfo.GetCultureInfo("de-AT")) == "Attribute verteilen",
    "de-AT must fall back to the German satellite catalog");
Assert(
    CreationAllocationStrings.Get(
        "Attributes.Heading",
        "fallback",
        CultureInfo.GetCultureInfo("es-MX")) == "Asignar atributos",
    "es-MX must fall back to the Spanish satellite catalog");
Assert(
    CreationAllocationStrings.Get(
        "Attributes.Heading",
        "fallback",
        CultureInfo.GetCultureInfo("fr-FR")) == "Allocate Attributes",
    "unsupported regional UI cultures must fail safely to neutral English");
Assert(
    CreationAllocationStrings.Format(
        CultureInfo.GetCultureInfo("de-AT"),
        "Attributes.Binding",
        "fallback",
        17,
        16,
        9) == "Revision 17 · gespeichert 16 · Voraussetzung-Entwurf 9",
    "regional formatting must preserve exact binding placeholders");
Assert(
    CreationAllocationStrings.Get(
        "Missing.Resource.Key",
        "safe English fallback",
        CultureInfo.GetCultureInfo("es-MX")) == "safe English fallback",
    "missing resources must return caller-provided safe English copy");

CultureInfo previousCulture = CultureInfo.CurrentUICulture;
try
{
    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-AT");
    Assert(
        CreationAllocationStrings.AttributeName("AGI") == "Geschicklichkeit",
        "known typed Attribute IDs must receive localized display copy");
    Assert(
        CreationAllocationStrings.AttributeName("CUSTOM_ATTRIBUTE") == "CUSTOM_ATTRIBUTE",
        "unknown typed Attribute IDs must remain exact and must never be translated heuristically");
}
finally
{
    CultureInfo.CurrentUICulture = previousCulture;
}

string[] surfaceFiles =
[
    "CreationAttributesPage.cs",
    "CreationSkillsPage.cs",
    "CreationMetatypePage.cs",
    "CreationMetatypePreviewPage.cs"
];
Dictionary<string, string> usedCopy = ReadSourceCopy(native, surfaceFiles);
foreach ((string key, string fallback) in ReadSourceCopy(
             native,
             ["CreationAllocationStrings.cs"]))
{
    usedCopy.TryAdd(key, fallback);
}
string[] missingUsedKeys = usedCopy.Keys
    .Except(neutral.Keys, StringComparer.Ordinal)
    .Order(StringComparer.Ordinal)
    .ToArray();
string[] unusedKeys = neutral.Keys
    .Except(usedCopy.Keys, StringComparer.Ordinal)
    .Order(StringComparer.Ordinal)
    .ToArray();
string[] mismatchedFallbacks = usedCopy
    .Where(pair => !string.Equals(neutral.GetValueOrDefault(pair.Key), pair.Value, StringComparison.Ordinal))
    .Select(pair => pair.Key)
    .Order(StringComparer.Ordinal)
    .ToArray();
Assert(
    missingUsedKeys.Length == 0,
    "resource calls missing from neutral catalog: " + string.Join(", ", missingUsedKeys));
Assert(
    unusedKeys.Length == 0,
    "unused allocation resource keys must be removed: " + string.Join(", ", unusedKeys));
Assert(
    mismatchedFallbacks.Length == 0,
    "English call-site fallbacks must match neutral resources: " + string.Join(", ", mismatchedFallbacks));

AssertNoDirectVisibleCopy(native, surfaceFiles);
AssertAuthorityBoundary(native);
AssertHelperScope(native, surfaceFiles);
AssertSourcesParse(native, surfaceFiles.Append("CreationAllocationStrings.cs"));

Console.WriteLine(
    $"Creation allocation localization tests passed ({neutral.Count} parity-checked keys; "
    + "en-GB/de-AT/es-MX fallback, English fail-safe, source boundaries, and syntax verified)." );
return;

static Dictionary<string, string> ReadResource(string path)
    => XDocument.Load(path)
        .Root!
        .Elements("data")
        .ToDictionary(
            element => (string)element.Attribute("name")!,
            element => element.Element("value")?.Value ?? string.Empty,
            StringComparer.Ordinal);

static void AssertSameKeys(
    IReadOnlyDictionary<string, string> neutral,
    IReadOnlyDictionary<string, string> localized,
    string culture)
{
    string[] missing = neutral.Keys.Except(localized.Keys, StringComparer.Ordinal).Order().ToArray();
    string[] extra = localized.Keys.Except(neutral.Keys, StringComparer.Ordinal).Order().ToArray();
    Assert(
        missing.Length == 0 && extra.Length == 0,
        $"{culture} key parity failed; missing [{string.Join(", ", missing)}], "
        + $"extra [{string.Join(", ", extra)}]");
    Assert(
        localized.All(static pair => !string.IsNullOrWhiteSpace(pair.Value)),
        $"{culture} must not contain blank translations");
}

static void AssertSamePlaceholders(
    IReadOnlyDictionary<string, string> neutral,
    IReadOnlyDictionary<string, string> localized,
    string culture)
{
    foreach ((string key, string value) in neutral)
    {
        string[] expected = Placeholders(value);
        string[] actual = Placeholders(localized[key]);
        Assert(
            expected.SequenceEqual(actual, StringComparer.Ordinal),
            $"{culture} placeholder parity failed for {key}: "
            + $"expected [{string.Join(", ", expected)}], actual [{string.Join(", ", actual)}]");
    }
}

static string[] Placeholders(string value)
    => Regex.Matches(value, @"\{\d+[^}]*\}")
        .Select(match => match.Value)
        .Order(StringComparer.Ordinal)
        .ToArray();

static Dictionary<string, string> ReadSourceCopy(string native, IEnumerable<string> files)
{
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    var pattern = new Regex(
        @"(?:CreationAllocationStrings\.)?(?:Get|Format)\(\s*""([^""]+)""\s*,\s*""((?:[^""\\]|\\.)*)""",
        RegexOptions.Singleline);
    foreach (string file in files)
    {
        foreach (Match match in pattern.Matches(File.ReadAllText(Path.Combine(native, file))))
        {
            string key = match.Groups[1].Value;
            string fallback = Regex.Unescape(match.Groups[2].Value);
            if (result.TryGetValue(key, out string? previous))
                Assert(previous == fallback, $"conflicting English fallbacks for {key}");
            else
                result.Add(key, fallback);
        }
    }
    return result;
}

static void AssertNoDirectVisibleCopy(string native, IEnumerable<string> files)
{
    var directVisibleCopy = new Regex(
        @"(?:Title\s*=|NativeTheme\.(?:Eyebrow|Title|Body|Metric|PrimaryButton|SecondaryButton|NavigationRow)\(|SemanticProperties\.SetDescription\([^,]+,)\s*\$?""",
        RegexOptions.Singleline);
    foreach (string file in files)
    {
        string source = File.ReadAllText(Path.Combine(native, file));
        source = source.Replace("NativeTheme.Body($\"• {blocker}\"", "NativeTheme.Body(blocker");
        Assert(
            !directVisibleCopy.IsMatch(source),
            $"{file} contains direct visible copy outside CreationAllocationStrings");
    }
}

static void AssertAuthorityBoundary(string native)
{
    string attributes = File.ReadAllText(Path.Combine(native, "CreationAttributesPage.cs"));
    string skills = File.ReadAllText(Path.Combine(native, "CreationSkillsPage.cs"));
    string metatype = File.ReadAllText(Path.Combine(native, "CreationMetatypePage.cs"));
    string metatypePreview = File.ReadAllText(Path.Combine(native, "CreationMetatypePreviewPage.cs"));

    Assert(
        attributes.Contains("Coordinator.PreviewCreationAttributes(state.Binding, allocations)", StringComparison.Ordinal)
        && attributes.Contains("Coordinator.ConfirmCreationAttributesAsync(\n                _preview,\n                _allocations)", StringComparison.Ordinal)
        && attributes.Contains("CreationPrerequisiteDigestText.CanonicalPrefix(_preview.PreviewDigest)", StringComparison.Ordinal),
        "Attribute preview/confirm/digest authority must remain exact");
    Assert(
        skills.Contains("Coordinator.PreviewCreationSkills(state.Binding, skills, groups)", StringComparison.Ordinal)
        && skills.Contains("Coordinator.ConfirmCreationSkillsAsync(\n                _preview,\n                _allocations,\n                _groups,\n                _idempotencyKey)", StringComparison.Ordinal)
        && skills.Contains("NativeTheme.Title(source.Name, 18)", StringComparison.Ordinal)
        && skills.Contains("NativeTheme.Title(skill.Name, 18)", StringComparison.Ordinal),
        "Skills authority calls and Core-projected dynamic names must remain exact");
    Assert(
        metatype.Contains("option.Label,", StringComparison.Ordinal)
        && metatype.Contains("option.OptionId", StringComparison.Ordinal)
        && metatypePreview.Contains("_draft.TryConfirmMetatype(state, _candidateOptionId)", StringComparison.Ordinal)
        && metatypePreview.Contains("NativeTheme.Title(option.Label, 22)", StringComparison.Ordinal),
        "Metatype identity, dynamic label, and explicit confirmation must remain exact");
}

static void AssertHelperScope(string native, IReadOnlyCollection<string> surfaceFiles)
{
    string[] expected = surfaceFiles.Append("CreationAllocationStrings.cs")
        .Order(StringComparer.Ordinal)
        .ToArray();
    string[] actual = Directory.EnumerateFiles(native, "*.cs", SearchOption.TopDirectoryOnly)
        .Where(path => File.ReadAllText(path).Contains("CreationAllocationStrings", StringComparison.Ordinal))
        .Select(Path.GetFileName)
        .OfType<string>()
        .Order(StringComparer.Ordinal)
        .ToArray();
    Assert(
        expected.SequenceEqual(actual, StringComparer.Ordinal),
        "allocation copy helper crossed its source boundary: " + string.Join(", ", actual));
}

static void AssertSourcesParse(string native, IEnumerable<string> files)
{
    foreach (string file in files)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(
            File.ReadAllText(Path.Combine(native, file)),
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        string[] errors = tree.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(static diagnostic => diagnostic.ToString())
            .ToArray();
        Assert(errors.Length == 0, $"{file} has syntax errors: {string.Join(" | ", errors)}");
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
