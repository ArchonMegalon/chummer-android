using System.Globalization;
using Chummer.Android.Native;
using Chummer.Contracts.Characters;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    private static void AssertCompletionLossDisclosure(CreationCompletionPage page,
        CharacterCreationCompletionFinalizationProjection actualProjection)
    {
        var expected = ExpectedCompletionLossItems(actualProjection);
        var actual = CreationCompletionPage.LossItems(actualProjection);
        Require(actual.SequenceEqual(expected),
            "Completion loss display does not match the exact reviewed pools and carryover differences.");
        var labels = MinimalVisible(page).OfType<Label>().ToArray();
        var rows = labels.Where(label => label.AutomationId?.StartsWith(
            "creation-completion-loss-", StringComparison.Ordinal) == true).ToArray();
        Require(rows.Length == expected.Count, "Loss disclosure hid a positive loss or added a zero-loss row.");
        foreach (var loss in expected)
        {
            var row = rows.SingleOrDefault(label => label.AutomationId == "creation-completion-loss-" + loss.Id);
            Require(row is not null && row.Text == CompletionLossLine(loss)
                && row.TextColor == NativeTheme.Danger,
                "Loss disclosure did not show the exact localized amount for " + loss.Id + ".");
        }
        string title = RequiredLossCopy("Title").ToUpperInvariant(), help = RequiredLossCopy("Help");
        Require(labels.Count(label => label.Text == title) == (expected.Count > 0 ? 1 : 0)
            && labels.Count(label => label.Text == help) == (expected.Count > 0 ? 1 : 0),
            "Loss disclosure must explain actual positive losses, not imply all remaining resources are discarded.");
        Require(CompletionPageControl<Button>(page, "creation-completion-apply").IsEnabled,
            "Legal unused pools must allow an explicit decision, not become a mandatory spend-all gate.");

        // These detached values test display arithmetic only. They are not
        // admitted reviews and must never be supplied to an Apply/coordinator.
        string originalDigest = CharacterCreationCompletionFinalizationRules.ComputeProjectionDigest(actualProjection);
        var noLoss = actualProjection with
        {
            UnusedPoints = new(0, 0, 0, 0, 0),
            KarmaBeforeCarryover = 7, KarmaRemaining = 7,
            NuyenBeforeCarryover = 500m, CarriedNuyen = 500m,
            StartingNuyen = 1000m, NuyenRemaining = 1500m,
            ProjectionDigest = string.Empty
        };
        Require(CreationCompletionPage.LossItems(noLoss).Count == 0,
            "Legal nonzero Karma/nuyen carryover or newly added starting cash was reported as lost.");
        var cashMasksLoss = noLoss with
        {
            UnusedPoints = new(1, 2, 3, 4, 5),
            KarmaBeforeCarryover = 27, KarmaRemaining = 7,
            NuyenBeforeCarryover = 1500m, CarriedNuyen = 500m,
            StartingNuyen = 5000m, NuyenRemaining = 5500m
        };
        var maskedLosses = CreationCompletionPage.LossItems(cashMasksLoss);
        Require(maskedLosses.Select(loss => (loss.Id, loss.Amount)).SequenceEqual(new (string Id, decimal Amount)[]
            {
                ("normal-attributes", 1m), ("special-attributes", 2m), ("active-skills", 3m),
                ("skill-groups", 4m), ("knowledge-skills", 5m), ("karma", 20m), ("nuyen", 1000m)
            }),
            "Starting cash concealed discarded creation nuyen, or positive unused pools/Karma were miscounted.");
        Require(CharacterCreationCompletionFinalizationRules.ComputeProjectionDigest(actualProjection) == originalDigest,
            "Loss-formatting checks altered the real admitted projection.");
    }

    private static string ExpectedCompletionLossMessage(CharacterCreationCompletionFinalizationProjection projection)
        => string.Join(Environment.NewLine, ExpectedCompletionLossItems(projection).Select(CompletionLossLine))
            + Environment.NewLine + Environment.NewLine + RequiredLossCopy("Help");

    private static IReadOnlyList<(string Id, string Label, decimal Amount)> ExpectedCompletionLossItems(
        CharacterCreationCompletionFinalizationProjection projection)
    {
        // Independently enumerate the reviewed fields; do not derive the expected
        // warning or dialog text by calling the production LossItems helper.
        (string Id, string Key, decimal Amount)[] fields =
        [
            ("normal-attributes", "NormalAttributes", projection.UnusedPoints.NormalAttributes),
            ("special-attributes", "SpecialAttributes", projection.UnusedPoints.SpecialAttributes),
            ("active-skills", "ActiveSkills", projection.UnusedPoints.ActiveSkills),
            ("skill-groups", "SkillGroups", projection.UnusedPoints.SkillGroups),
            ("knowledge-skills", "KnowledgeSkills", projection.UnusedPoints.KnowledgeSkills),
            ("karma", "Karma", projection.KarmaBeforeCarryover - projection.KarmaRemaining),
            ("nuyen", "Nuyen", projection.NuyenBeforeCarryover - projection.CarriedNuyen)
        ];
        return fields.Where(field => field.Amount > 0)
            .Select(field => (field.Id, RequiredLossCopy(field.Key), field.Amount)).ToArray();
    }

    private static string CompletionLossLine((string Id, string Label, decimal Amount) loss)
        => loss.Label + ": " + loss.Amount.ToString("0.##", CultureInfo.CurrentCulture);

    private static string RequiredLossCopy(string key)
    {
        const string missing = "missing-completion-loss-copy";
        string value = CreationCompletionPage.Copy("Loss." + key, missing);
        Require(value != missing && !string.IsNullOrWhiteSpace(value), "Missing localized loss explanation: " + key);
        return value;
    }
}
