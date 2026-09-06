using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Chummer.Android.Native;

string workspace = Directory.GetCurrentDirectory();
string localization = Path.Combine(
    workspace,
    "src",
    "Chummer.Android",
    "Resources",
    "Localization");
string neutralPath = Path.Combine(localization, "WizardStrings.resx");
string germanPath = Path.Combine(localization, "WizardStrings.de.resx");
string spanishPath = Path.Combine(localization, "WizardStrings.es.resx");

Dictionary<string, string> neutral = ReadResource(neutralPath);
Dictionary<string, string> german = ReadResource(germanPath);
Dictionary<string, string> spanish = ReadResource(spanishPath);
Assert(neutral.Count > 0, "neutral WizardStrings resources must not be empty");
AssertSameKeys(neutral, german, "de");
AssertSameKeys(neutral, spanish, "es");
AssertSamePlaceholders(neutral, german, "de");
AssertSamePlaceholders(neutral, spanish, "es");

Assert(
    WizardStrings.Get(
        "Career.Heading",
        "fallback",
        CultureInfo.GetCultureInfo("en-GB")) == "Career wizard",
    "en-GB must fall back to the neutral English resource");
Assert(
    WizardStrings.Get(
        "Career.Heading",
        "fallback",
        CultureInfo.GetCultureInfo("de-AT")) == "Karriereassistent",
    "de-AT must fall back to the German satellite resource");
Assert(
    WizardStrings.Get(
        "Career.Heading",
        "fallback",
        CultureInfo.GetCultureInfo("es-MX")) == "Asistente de carrera",
    "es-MX must fall back to the Spanish satellite resource");
Assert(
    WizardStrings.Format(
        CultureInfo.GetCultureInfo("de-AT"),
        "Priority.Binding",
        "fallback",
        7,
        6,
        "abc",
        "def") == "Revision 7 · gespeichert 6 · Snapshot abc · Instanz def",
    "regional formatting must preserve binding placeholders");
Assert(
    WizardStrings.Get(
        "Missing.Resource.Key",
        "safe fallback",
        CultureInfo.GetCultureInfo("es-MX")) == "safe fallback",
    "missing resources must fail closed to the supplied copy");

CultureInfo original = CultureInfo.CurrentUICulture;
try
{
    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-AT");
    Assert(
        WizardStrings.PriorityCategory("ATTRIBUTES", "fallback") == "Attribute",
        "dynamic Priority category keys must be culture-backed and case-stable");
    Assert(
        WizardStrings.PriorityHeritageKind("METAVARIANT", "fallback") == "Metavariante",
        "dynamic Heritage kind keys must be culture-backed and case-stable");
    Assert(
        WizardStrings.CareerFamilyTitle("career.advancement", "fallback") == "Steigerung",
        "dynamic Career family keys must be culture-backed");
    Assert(
        WizardStrings.CareerActionTitle("career.attribute.advance", "fallback")
            == "Attribut steigern",
        "dynamic Career action keys must be culture-backed");
}
finally
{
    CultureInfo.CurrentUICulture = original;
}

HashSet<string> usedKeys = ReadSourceKeys(workspace);
foreach (string dynamicKey in DynamicKeys())
    usedKeys.Add(dynamicKey);
string[] missingUsedKeys = usedKeys.Except(neutral.Keys, StringComparer.Ordinal).Order().ToArray();
Assert(
    missingUsedKeys.Length == 0,
    "all wizard resource calls must resolve in the neutral catalog: "
    + string.Join(", ", missingUsedKeys));

Console.WriteLine(
    $"Wizard localization tests passed ({neutral.Count} parity-checked keys; "
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
        "CreationPrerequisitePage.cs",
        "CreationPriorityCategoryPage.cs",
        "CreationPriorityDetailPage.cs",
        "CreationPrerequisitePreviewPage.cs",
        "Sr5CareerWizardPage.cs",
        "CurrentPhoneWizardScope.cs"
    ];
    var result = new HashSet<string>(StringComparer.Ordinal);
    var pattern = new Regex(@"WizardStrings\.(?:Get|Format)\(\s*""([^""]+)""");
    foreach (string file in files)
    {
        foreach (Match match in pattern.Matches(File.ReadAllText(Path.Combine(native, file))))
            result.Add(match.Groups[1].Value);
    }
    return result;
}

static IEnumerable<string> DynamicKeys()
{
    foreach (string category in new[] { "heritage", "talent", "attributes", "skills", "resources" })
        yield return $"Priority.Category.{category}";
    foreach (string kind in new[] { "metatype", "metavariant" })
        yield return $"Priority.HeritageKind.{kind}";
    foreach (string family in new[] { "career.economy", "career.advancement", "career.calendar" })
    {
        yield return $"Career.Family.{family}.Title";
        yield return $"Career.Family.{family}.Detail";
    }
    foreach (string action in new[]
             {
                 "career.karma.adjust",
                 "career.nuyen.adjust",
                 "career.karma-expense.edit",
                 "career.nuyen-expense.edit",
                 "career.attribute.advance",
                 "career.active-skill.advance",
                 "career.knowledge-skill.advance",
                 "career.skill-group.advance",
                 "career.skill-specialization.learn",
                 "career.quality.change",
                 "career.calendar-entry.manage"
             })
    {
        yield return $"Career.Action.{action}.Title";
        yield return $"Career.Action.{action}.Detail";
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
