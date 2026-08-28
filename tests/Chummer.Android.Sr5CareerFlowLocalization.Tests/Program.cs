using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Chummer.Android.Native;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

string workspace = FindWorkspace();
string localization = Path.Combine(
    workspace,
    "src",
    "Chummer.Android",
    "Resources",
    "Localization");
Dictionary<string, string> neutral = ReadResource(
    Path.Combine(localization, "Sr5CareerFlowStrings.resx"));
Dictionary<string, string> german = ReadResource(
    Path.Combine(localization, "Sr5CareerFlowStrings.de.resx"));
Dictionary<string, string> spanish = ReadResource(
    Path.Combine(localization, "Sr5CareerFlowStrings.es.resx"));

Assert(neutral.Count > 0, "neutral Sr5CareerFlowStrings catalog must not be empty");
Assert(
    neutral.All(static pair => pair.Key == pair.Value),
    "neutral values must equal keys");
Assert(
    neutral.All(static pair => !string.IsNullOrWhiteSpace(pair.Value)),
    "neutral must not contain blank values");
AssertSameKeys(neutral, german, "de");
AssertSameKeys(neutral, spanish, "es");
AssertSamePlaceholders(neutral, german, "de");
AssertSamePlaceholders(neutral, spanish, "es");
AssertSameLayoutTokens(neutral, german, "de");
AssertSameLayoutTokens(neutral, spanish, "es");

Assert(
    Sr5CareerFlowStrings.Text("Advance attribute", CultureInfo.GetCultureInfo("en-GB"))
        == "Advance attribute",
    "en-GB must fall back to the neutral English catalog");
Assert(
    Sr5CareerFlowStrings.Text("Advance attribute", CultureInfo.GetCultureInfo("de-AT"))
        == "Attribut steigern",
    "de-AT must fall back to the German satellite catalog");
Assert(
    Sr5CareerFlowStrings.Text("Advance attribute", CultureInfo.GetCultureInfo("es-MX"))
        == "Mejorar un atributo",
    "es-MX must fall back to the Spanish satellite catalog");
Assert(
    Sr5CareerFlowStrings.Format(
        CultureInfo.GetCultureInfo("de-AT"),
        "Cost {0} Karma · available {1} · after {2}",
        5,
        10,
        5) == "Kosten 5 Karma · verfügbar 10 · danach 5",
    "regional formatting must preserve exact placeholders");
Assert(
    Sr5CareerFlowStrings.Format(
        CultureInfo.GetCultureInfo("es-MX"),
        "Cost {0} Karma · available {1} · after {2}",
        5,
        10,
        5) == "Coste 5 Karma · disponible 10 · después 5",
    "Spanish regional formatting must preserve exact placeholders");
Assert(
    Sr5CareerFlowStrings.Text(
        "Missing.Resource.Key",
        CultureInfo.GetCultureInfo("es-MX")) == "Missing.Resource.Key",
    "missing resources must fail closed to the English source");

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
    "unused Sr5CareerFlowStrings keys must be removed: " + string.Join(", ", unusedKeys));
AssertSourceBoundary(workspace);
AssertSourcesParse(workspace);

Console.WriteLine(
    $"SR5 career flow localization tests passed ({neutral.Count} parity-checked keys; "
    + "en-GB/de-AT/es-MX fallback verified)." );
return;

static string FindWorkspace()
{
    foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        DirectoryInfo? dir = new(start);
        while (dir is not null)
        {
            string candidate = Path.Combine(
                dir.FullName,
                "src",
                "Chummer.Android",
                "Resources",
                "Localization",
                "Sr5CareerFlowStrings.resx");
            if (File.Exists(candidate))
                return dir.FullName;
            dir = dir.Parent;
        }
    }

    throw new InvalidOperationException(
        "workspace root with Sr5CareerFlowStrings.resx was not found");
}

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

static void AssertSameLayoutTokens(
    IReadOnlyDictionary<string, string> neutral,
    IReadOnlyDictionary<string, string> localized,
    string culture)
{
    foreach ((string key, string value) in neutral)
    {
        string translated = localized[key];
        Assert(
            LeadingWhitespace(value) == LeadingWhitespace(translated)
            && TrailingWhitespace(value) == TrailingWhitespace(translated)
            && Count(value, '\n') == Count(translated, '\n')
            && Count(value, '·') == Count(translated, '·')
            && Count(value, '→') == Count(translated, '→')
            && Count(value, '…') == Count(translated, '…'),
            $"{culture} layout-token parity failed for {key}");
    }
}

static int LeadingWhitespace(string value) => value.Length - value.TrimStart().Length;

static int TrailingWhitespace(string value) => value.Length - value.TrimEnd().Length;

static int Count(string value, char token) => value.Count(character => character == token);

static string[] Placeholders(string value)
    => Regex.Matches(value, @"\{\d+[^}]*\}")
        .Select(match => match.Value)
        .Order(StringComparer.Ordinal)
        .ToArray();

static string[] TargetPages()
    =>
    [
        "Sr5CareerAttributeWizardPage.cs",
        "Sr5CareerActiveSkillWizardPage.cs",
        "Sr5CareerSkillGroupWizardPage.cs",
        "Sr5CareerKnowledgeSkillWizardPage.cs",
        "Sr5CareerSpecializationWizardPage.cs",
        "Sr5CareerQualityWizardPage.cs",
        "Sr5AfterRunSettlementWizardPage.cs",
        "Sr5DowntimeCalendarWizardPage.cs",
        "Sr5TableWizardPage.cs",
        "Sr5CareerCommercePages.cs"
    ];

static HashSet<string> ReadSourceKeys(string workspace)
{
    var result = new HashSet<string>(StringComparer.Ordinal);
    foreach (string file in TargetPages())
    {
        foreach (InvocationExpressionSyntax invocation in ParseInvocations(workspace, file))
        {
            foreach (string literal in TemplateLiterals(invocation))
                result.Add(literal);
        }
    }

    foreach (string key in Sr5CareerRunCapabilityCatalog.ResourceKeys)
        result.Add(key);

    return result;
}

static void AssertSourceBoundary(string workspace)
{
    var otherHelpers = new Regex(
        @"\b(WizardStrings|PhoneStrings|CreationFlowStrings|AndroidSurfaceStrings)\b");
    var rawChrome = new Regex(
        @"(?:Title\s*=|NativeTheme\.(?:Eyebrow|Title|Body|Metric|FieldLabel|PrimaryButton|SecondaryButton|NavigationRow)\(|DisplayAlertAsync\()\s*""");
    var interpolatedChrome = new Regex(
        @"(?:Title\s*=|NativeTheme\.(?:Eyebrow|Title|Body|Metric|FieldLabel|PrimaryButton|SecondaryButton|NavigationRow)\(|DisplayAlertAsync\()\s*\$""(?<template>(?:\\.|[^""])*)""");
    foreach (string file in TargetPages())
    {
        string source = File.ReadAllText(Path.Combine(NativeRoot(workspace), file));
        Assert(
            source.Contains(
                "using static Chummer.Android.Native.Sr5CareerFlowStrings;",
                StringComparison.Ordinal),
            $"{file} must use Sr5CareerFlowStrings");
        Assert(
            !otherHelpers.IsMatch(source),
            $"{file} must not call other localization helpers");
        Assert(
            !rawChrome.IsMatch(source),
            $"{file} contains direct visible copy outside Sr5CareerFlowStrings");
        foreach (Match match in interpolatedChrome.Matches(source))
        {
            string leftover = Regex.Replace(match.Groups["template"].Value, @"\{[^{}]+\}", "");
            Assert(
                !Regex.IsMatch(leftover, @"[A-Za-z]{2,}"),
                $"{file} interpolates chrome copy outside Sr5CareerFlowStrings: {match.Value}");
        }

        foreach (InvocationExpressionSyntax invocation in ParseInvocations(workspace, file, source))
        {
            ExpressionSyntax template = TemplateExpression(invocation);
            Assert(
                IsLiteralOrLiteralConditional(template),
                $"{file} passes a non-literal template through Text/Format: {template}");
            Assert(
                !ContainsCoreProjectedMember(template),
                $"{file} passes a Core-projected name, message, ID, or digest through Text/Format: {template}");
        }
    }
}

static void AssertSourcesParse(string workspace)
{
    foreach (string file in TargetPages()
                 .Append("Sr5CareerFlowStrings.cs")
                 .Append("Sr5CareerRunCapabilityCatalog.cs"))
    {
        SyntaxTree tree = ParseTree(File.ReadAllText(Path.Combine(NativeRoot(workspace), file)));
        string[] errors = tree.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(static diagnostic => diagnostic.ToString())
            .ToArray();
        Assert(errors.Length == 0, $"{file} has syntax errors: {string.Join(" | ", errors)}");
    }
}

static IEnumerable<InvocationExpressionSyntax> ParseInvocations(
    string workspace,
    string file,
    string? source = null)
{
    source ??= File.ReadAllText(Path.Combine(NativeRoot(workspace), file));
    foreach (InvocationExpressionSyntax invocation in ParseTree(source)
                 .GetRoot()
                 .DescendantNodes()
                 .OfType<InvocationExpressionSyntax>())
    {
        if (invocation.Expression is IdentifierNameSyntax name
            && name.Identifier.Text is "Text" or "Format")
        {
            yield return invocation;
        }
    }
}

static ExpressionSyntax TemplateExpression(InvocationExpressionSyntax invocation)
{
    SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;
    Assert(arguments.Count > 0, $"Text/Format call has no arguments: {invocation}");
    ExpressionSyntax first = arguments[0].Expression;
    if (IsLiteralOrLiteralConditional(first) || StringLiterals(first).Any())
        return first;
    if (arguments.Count > 1)
        return arguments[1].Expression;
    return first;
}

static IEnumerable<string> TemplateLiterals(InvocationExpressionSyntax invocation)
    => StringLiterals(TemplateExpression(invocation));

static IEnumerable<string> StringLiterals(ExpressionSyntax expression)
{
    switch (expression)
    {
        case LiteralExpressionSyntax literal
            when literal.IsKind(SyntaxKind.StringLiteralExpression)
                 && literal.Token.Value is string value:
            yield return value;
            yield break;
        case ParenthesizedExpressionSyntax parenthesized:
            foreach (string value in StringLiterals(parenthesized.Expression))
                yield return value;
            yield break;
        case ConditionalExpressionSyntax conditional:
            foreach (string value in StringLiterals(conditional.WhenTrue))
                yield return value;
            foreach (string value in StringLiterals(conditional.WhenFalse))
                yield return value;
            yield break;
        case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression):
            foreach (string value in StringLiterals(binary.Left))
                yield return value;
            foreach (string value in StringLiterals(binary.Right))
                yield return value;
            yield break;
        default:
            yield break;
    }
}

static bool IsLiteralOrLiteralConditional(ExpressionSyntax expression)
{
    return Unwrap(expression) switch
    {
        LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression)
            => true,
        ConditionalExpressionSyntax conditional
            => IsLiteralOrLiteralConditional(conditional.WhenTrue)
               && IsLiteralOrLiteralConditional(conditional.WhenFalse),
        BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression)
            => IsLiteralOrLiteralConditional(binary.Left)
               && IsLiteralOrLiteralConditional(binary.Right),
        _ => false
    };
}

static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    => expression is ParenthesizedExpressionSyntax parenthesized
        ? Unwrap(parenthesized.Expression)
        : expression;

static bool ContainsCoreProjectedMember(ExpressionSyntax expression)
{
    foreach (SyntaxNode node in expression.DescendantNodesAndSelf())
    {
        if (node is not MemberAccessExpressionSyntax member)
            continue;
        if (node.Ancestors().OfType<LiteralExpressionSyntax>().Any())
            continue;
        string name = member.Name.Identifier.Text;
        if (name is "Message"
            or "Name"
            or "SourceId"
            or "InternalId"
            or "Digest"
            or "RuleDigest"
            or "ContentDigest"
            or "RuntimeDigest"
            or "Blocker"
            or "Definition")
        {
            return true;
        }
    }

    return false;
}

static SyntaxTree ParseTree(string source)
    => CSharpSyntaxTree.ParseText(
        source,
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

static string NativeRoot(string workspace)
    => Path.Combine(workspace, "src", "Chummer.Android", "Native");

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
