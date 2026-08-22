using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// Narrow creation-only route into the existing typed attribute editor. It deliberately omits the
/// exhaustive section action catalog used by the post-creation Build surface.
/// </summary>
public sealed class CreationAttributesPage : NativePageBase
{
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };

    public CreationAttributesPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = "Attributes";
        AutomationId = "creation-wizard-attributes";
        ToolbarItems.Add(new ToolbarItem
        {
            Text = "Rook",
            AutomationId = "creation-attributes-rook",
            Command = new Command(async () => await Navigation.PushAsync(new RookConversationPage(Coordinator)))
        });
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Character creation"));
        _body.Add(NativeTheme.Title("Attributes"));

        if (Coordinator.State.Profile?.Created != false
            || Coordinator.State.CreationWizard is not { } snapshot)
        {
            _body.Add(NativeTheme.Body(
                "This creation step is no longer available. Return to Build for the current runner surface.",
                NativeTheme.Danger));
            return;
        }

        if (!AddBudget(snapshot))
        {
            AddRookEntry();
            return;
        }
        IReadOnlyList<AttributeWorkbenchRow> rows = AttributeWorkbenchProjector.BuildRows(
            Coordinator.State.ActiveSectionId,
            Coordinator.State.ActiveSectionJson ?? string.Empty);
        if (!AttributeWorkbenchProjector.IsAttributeSection(Coordinator.State.ActiveSectionId)
            || rows.Count == 0)
        {
            _body.Add(NativeTheme.Body(
                "The authoritative attribute section is unavailable. No values can be edited from this step.",
                NativeTheme.Danger));
            return;
        }

        _body.Add(NativeTheme.Eyebrow("Choose an attribute"));
        foreach (AttributeWorkbenchRow row in rows)
        {
            string detail = $"{row.TotalValue} · natural {row.MetatypeMin}–{row.MetatypeMax}";
            _body.Add(NativeTheme.NavigationRow(
                row.DisplayName,
                detail,
                () => Navigation.PushAsync(new AttributeEditPage(Coordinator, row)),
                automationId: $"creation-attribute-{Token(row.AttributeName)}"));
        }

        AddRookEntry();
    }

    private bool AddBudget(CharacterCreationWizardSnapshot snapshot)
    {
        CharacterCreationBudgetState? budget = snapshot.Budgets.FirstOrDefault(candidate =>
            string.Equals(candidate.BudgetId, CharacterCreationBudgetIds.NormalAttributes, StringComparison.Ordinal));
        if (budget is null)
        {
            _body.Add(NativeTheme.Body(
                "The rules authority did not project an attribute budget. Attribute editing stays unavailable.",
                NativeTheme.Danger));
            return false;
        }

        VerticalStackLayout card = new() { Spacing = 8 };
        card.Add(NativeTheme.Eyebrow("Attribute budget"));
        if (budget.IsExact)
        {
            string unit = string.IsNullOrWhiteSpace(budget.Unit) ? "points" : budget.Unit;
            card.Add(NativeTheme.Title(
                $"{budget.Remaining.ToString("0.##", CultureInfo.InvariantCulture)} {unit} left",
                22));
            card.Add(NativeTheme.Body(
                $"{budget.Used.ToString("0.##", CultureInfo.InvariantCulture)} used of "
                + budget.Total.ToString("0.##", CultureInfo.InvariantCulture),
                NativeTheme.Muted));
        }
        else
        {
            card.Add(NativeTheme.Title("Exact remainder unavailable", 21));
            card.Add(NativeTheme.Body(
                budget.Blockers.Count == 0
                    ? "The projection is not exact, so Chummer will not guess."
                    : string.Join("\n", budget.Blockers),
                NativeTheme.Danger));
        }
        _body.Add(NativeTheme.Card(card));
        return budget.IsExact;
    }

    private void AddRookEntry()
    {
        _body.Add(NativeTheme.NavigationRow(
            "Ask Rook",
            "Current revision, budgets, blockers, and legal options · local grounded fallback",
            () => Navigation.PushAsync(new RookConversationPage(Coordinator)),
            automationId: "creation-attributes-rook-entry"));
    }

    private static string Token(string value)
        => new(value.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());
}
