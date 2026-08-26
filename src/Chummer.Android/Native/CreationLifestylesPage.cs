using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>Catalog and existing-Lifestyle entry point for SR5 creation.</summary>
public sealed class CreationLifestylesPage : NativePageBase
{
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };

    public CreationLifestylesPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = "Creation lifestyles";
        AutomationId = "creation-lifestyles-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Character creation · Contacts/Lifestyles"));
        _body.Add(NativeTheme.Title("Lifestyles"));
        _body.Add(NativeTheme.Body(
            "Choose a Core catalog entry, configure a typed draft, inspect exact economics and the "
            + "preservation-bound atomic write plan, then confirm separately.",
            NativeTheme.Muted));

        CharacterCreationLifestylesInteractionLoadResult load = Coordinator.LoadCreationLifestyles();
        if (!string.Equals(load.Outcome, CharacterCreationLifestyleOutcomes.Available, StringComparison.Ordinal)
            || load.State is not { } state)
        {
            AddBlockers("Creation Lifestyles authority unavailable", load.Blockers, "creation-lifestyles-unavailable");
            return;
        }

        AddBinding(state);
        AddBudget(state.Budget, "creation-lifestyles-budget");
        if (!CreationLifestylesPhoneAuthority.IsReady(state, Coordinator.State))
        {
            AddBlockers(
                "Creation Lifestyles authority blocked",
                state.Blockers.DefaultIfEmpty(CharacterCreationLifestylesBlockers.AuthorityUnavailable).ToArray(),
                "creation-lifestyles-blockers");
            return;
        }

        AddExisting(state);
        AddCatalog(state);
        AddAuthority(state);
    }

    private void AddBinding(CharacterCreationLifestylesInteractionState state)
    {
        Label binding = NativeTheme.Body(
            $"Revision {state.Binding.ContentRevision} · saved {state.Binding.SavedRevision} · "
            + $"snapshot {ShortDigest(state.SnapshotDigest)} · source {ShortDigest(state.Binding.SourceDigest)}",
            NativeTheme.Muted);
        binding.AutomationId = "creation-lifestyles-binding";
        _body.Add(binding);
    }

    private void AddBudget(CharacterCreationLifestyleBudget budget, string automationId)
    {
        VerticalStackLayout card = new() { Spacing = 7 };
        card.Add(NativeTheme.Eyebrow("Starting nuyen budget"));
        card.Add(NativeTheme.Metric("Total", budget.Total.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric("Used", budget.Used.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric("Remaining", budget.Remaining.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric("Overspend", budget.Overspend.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Body(
            budget.IsExact ? "Exact Core budget" : "Budget authority is not exact",
            budget.IsExact ? NativeTheme.Muted : NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = automationId;
        SemanticProperties.SetDescription(
            border,
            $"Starting nuyen. Total {budget.Total}. Used {budget.Used}. Remaining {budget.Remaining}.");
        _body.Add(border);
    }

    private void AddExisting(CharacterCreationLifestylesInteractionState state)
    {
        _body.Add(NativeTheme.Eyebrow("Current Lifestyles"));
        if (state.Lifestyles.Count == 0)
        {
            Label empty = NativeTheme.Body("No Lifestyle selected yet.", NativeTheme.Muted);
            empty.AutomationId = "creation-lifestyles-empty";
            _body.Add(NativeTheme.Card(empty));
            return;
        }

        foreach (CharacterCreationLifestyleProjection lifestyle in state.Lifestyles
                     .OrderBy(item => item.Configuration.Name, StringComparer.Ordinal))
        {
            _body.Add(NativeTheme.NavigationRow(
                lifestyle.Configuration.Name,
                $"{lifestyle.BaseLifestyleName} · {lifestyle.Economics.TotalCost.ToString(CultureInfo.InvariantCulture)} nuyen · "
                + $"authority {ShortDigest(lifestyle.LifestyleDigest)}",
                () => Navigation.PushAsync(new CreationLifestyleEditPage(
                    Coordinator,
                    lifestyle.Configuration.LifestyleId,
                    optionId: null,
                    isCreate: false)),
                automationId: $"creation-lifestyle-item-{lifestyle.Configuration.LifestyleId:N}"));
        }
    }

    private void AddCatalog(CharacterCreationLifestylesInteractionState state)
    {
        _body.Add(NativeTheme.Eyebrow("Add from exact Core catalog"));
        foreach (CharacterCreationLifestyleCatalogOption option in state.Authority.LifestyleOptions
                     .OrderBy(item => item.BaseCost)
                     .ThenBy(item => item.Name, StringComparer.Ordinal))
        {
            bool enabled = option.IsSelectable && option.EligibilityIsExact && option.Blockers.Count == 0;
            string detail = enabled
                ? $"{option.BaseCost.ToString(CultureInfo.InvariantCulture)} per {option.DefaultIncrementId} · "
                  + $"{option.SourceBook} {option.Page}"
                : option.Blockers.FirstOrDefault() ?? CharacterCreationLifestylesBlockers.UnsupportedSemantics;
            Guid lifestyleId = Guid.NewGuid();
            _body.Add(NativeTheme.NavigationRow(
                option.Name,
                detail,
                () => Navigation.PushAsync(new CreationLifestyleEditPage(
                    Coordinator,
                    lifestyleId,
                    option.OptionId,
                    isCreate: true)),
                enabled: enabled,
                automationId: $"creation-lifestyle-catalog-{Token(option.OptionId)}"));
        }
    }

    private void AddAuthority(CharacterCreationLifestylesInteractionState state)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow("Authority binding"));
        card.Add(NativeTheme.Metric("Profile", state.Authority.SettingsProfileId));
        card.Add(NativeTheme.Metric("Lifestyle options", state.Authority.LifestyleOptions.Count.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric("Quality options", state.Authority.QualityOptions.Count.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric("Rules", state.Binding.RulesDigest));
        card.Add(NativeTheme.Metric("Runtime", state.Binding.RuntimeDigest));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-lifestyles-authority";
        _body.Add(border);
    }

    private void AddBlockers(string title, IReadOnlyList<string> blockers, string automationId)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(title));
        foreach (string blocker in blockers.DefaultIfEmpty(
                     CharacterCreationLifestylesBlockers.AuthorityUnavailable).Distinct(StringComparer.Ordinal))
        {
            card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        }
        Border border = NativeTheme.Card(card);
        border.AutomationId = automationId;
        _body.Add(border);
    }

    private static string Token(string value)
        => new(value.Select(character => char.IsLetterOrDigit(character)
            ? char.ToLowerInvariant(character)
            : '-').ToArray()).Trim('-');

    private static string ShortDigest(string value)
        => string.IsNullOrWhiteSpace(value) ? "unavailable" : value[..Math.Min(19, value.Length)];
}
