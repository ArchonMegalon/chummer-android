using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Chummer.Android.Native;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

string workspace = Directory.GetCurrentDirectory();
string localization = Path.Combine(
    workspace,
    "src",
    "Chummer.Android",
    "Resources",
    "Localization");
Dictionary<string, string> neutral = ReadResource(
    Path.Combine(localization, "CreationFlowStrings.resx"));
Dictionary<string, string> german = ReadResource(
    Path.Combine(localization, "CreationFlowStrings.de.resx"));
Dictionary<string, string> spanish = ReadResource(
    Path.Combine(localization, "CreationFlowStrings.es.resx"));

Assert(neutral.Count > 0, "neutral CreationFlowStrings catalog must not be empty");
AssertSameKeys(neutral, german, "de");
AssertSameKeys(neutral, spanish, "es");
AssertSamePlaceholders(neutral, german, "de");
AssertSamePlaceholders(neutral, spanish, "es");

Assert(
    CreationFlowStrings.Get(
        "Contacts.Heading",
        "fallback",
        CultureInfo.GetCultureInfo("en-GB")) == "Contacts",
    "en-GB must use the neutral English catalog");
Assert(
    CreationFlowStrings.Get(
        "Contacts.Heading",
        "fallback",
        CultureInfo.GetCultureInfo("de-AT")) == "Kontakte",
    "de-AT must fall back to the German satellite catalog");
Assert(
    CreationFlowStrings.Get(
        "Contacts.Heading",
        "fallback",
        CultureInfo.GetCultureInfo("es-MX")) == "Contactos",
    "es-MX must fall back to the Spanish satellite catalog");
Assert(
    CreationFlowStrings.Format(
        CultureInfo.GetCultureInfo("de-AT"),
        "Common.Binding",
        "fallback",
        17,
        16,
        "abc",
        "def") == "Revision 17 · gespeichert 16 · Snapshot abc · Quelle def",
    "regional formatting must preserve exact binding placeholders");
Assert(
    CreationFlowStrings.Get(
        "Missing.Resource.Key",
        "safe fallback",
        CultureInfo.GetCultureInfo("es-MX")) == "safe fallback",
    "missing resources must return the caller-provided safe copy");

HashSet<string> usedKeys = ReadSourceKeys(workspace);
string[] missingUsedKeys = usedKeys
    .Except(neutral.Keys, StringComparer.Ordinal)
    .Order(StringComparer.Ordinal)
    .ToArray();
string[] unusedKeys = neutral.Keys
    .Except(usedKeys, StringComparer.Ordinal)
    .Order(StringComparer.Ordinal)
    .ToArray();
Assert(
    missingUsedKeys.Length == 0,
    "resource calls missing from the neutral catalog: " + string.Join(", ", missingUsedKeys));
Assert(
    unusedKeys.Length == 0,
    "unused CreationFlowStrings keys must be removed: " + string.Join(", ", unusedKeys));
AssertNoDirectVisibleCopy(workspace);
AssertSourcesParse(workspace);

Console.WriteLine(
    $"Creation flow localization tests passed ({neutral.Count} parity-checked keys; "
    + "en-GB/de-AT/es-MX fallback verified)." );
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

static HashSet<string> ReadSourceKeys(string workspace)
{
    string native = Path.Combine(workspace, "src", "Chummer.Android", "Native");
    string[] files =
    [
        "CreationContactsPage.cs",
        "CreationLifestylesPage.cs",
        "CreationQualitiesPage.cs",
        "CreationMagicResonancePage.cs"
    ];
    var result = new HashSet<string>(StringComparer.Ordinal);
    var pattern = new Regex(@"CreationFlowStrings\.(?:Get|Format)\(\s*""([^""]+)""");
    foreach (string file in files)
    {
        foreach (Match match in pattern.Matches(File.ReadAllText(Path.Combine(native, file))))
            result.Add(match.Groups[1].Value);
    }
    return result;
}

static void AssertNoDirectVisibleCopy(string workspace)
{
    string native = Path.Combine(workspace, "src", "Chummer.Android", "Native");
    string[] files =
    [
        "CreationContactsPage.cs",
        "CreationLifestylesPage.cs",
        "CreationQualitiesPage.cs",
        "CreationMagicResonancePage.cs"
    ];
    var directVisibleCopy = new Regex(
        @"(?:Title\s*=|NativeTheme\.(?:Eyebrow|Title|Body|Metric|PrimaryButton|SecondaryButton|NavigationRow)\(|DisplayAlertAsync\()\s*\$?""");
    foreach (string file in files)
    {
        string source = File.ReadAllText(Path.Combine(native, file));
        source = source.Replace(
            "NativeTheme.Body($\"• {blocker}\"",
            "NativeTheme.Body(blocker");
        Assert(
            !directVisibleCopy.IsMatch(source),
            $"{file} contains direct visible copy outside CreationFlowStrings");
    }
}

static void AssertSourcesParse(string workspace)
{
    string native = Path.Combine(workspace, "src", "Chummer.Android", "Native");
    string[] files =
    [
        "CreationContactsPage.cs",
        "CreationLifestylesPage.cs",
        "CreationQualitiesPage.cs",
        "CreationMagicResonancePage.cs",
        "CreationFlowStrings.cs"
    ];
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
