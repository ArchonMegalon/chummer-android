using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// Phone-only deep-navigation list over the exact metatype options projected by Presentation.
/// Disabled options remain visible with their original authority reason and cannot be selected.
/// </summary>
public sealed class CreationMetatypePage : NativePageBase
{
    private readonly CreationFoundationPhoneDraft _draft;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };

    internal CreationMetatypePage(
        RunnerSessionCoordinator coordinator,
        CreationFoundationPhoneDraft draft) : base(coordinator)
    {
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        Title = "Metatype";
        AutomationId = "creation-metatype-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Foundation"));
        _body.Add(NativeTheme.Title("Choose a metatype"));

        CharacterCreationFoundationInteractionLoadResult load = Coordinator.LoadCreationFoundation();
        if (Coordinator.State.Profile?.Created != false
            || !string.Equals(load.Outcome, CharacterCreationFoundationOutcomes.Success, StringComparison.Ordinal)
            || load.State is not { } state)
        {
            AddBlockers(
                "Metatype authority unavailable",
                load.Blockers.Count > 0 ? load.Blockers : [load.Outcome]);
            return;
        }

        if (!_draft.Matches(state))
        {
            AddBlockers(
                "Metatype selection is stale",
                ["The Foundation workspace, revision, or digest changed. Return and reload before selecting."]);
            return;
        }

        Label binding = NativeTheme.Body(
            $"Revision {state.Binding.ContentRevision} · saved {state.Binding.SavedRevision} · "
            + $"snapshot {ShortDigest(state.FoundationSnapshotDigest)}",
            NativeTheme.Muted);
        binding.AutomationId = "creation-metatype-binding";
        _body.Add(binding);

        AddBudget(state.LifeModuleBudget);
        if (state.AuthorityBlockers.Count > 0)
            AddBlockers("Authority blockers", state.AuthorityBlockers);

        if (state.MetatypeOptions.Count == 0)
        {
            AddBlockers("No legal options", ["No metatype options were projected by the authority."]);
            return;
        }

        _body.Add(NativeTheme.Eyebrow("Authoritative options"));
        foreach (CharacterCreationLegalOption option in state.MetatypeOptions)
        {
            bool uniquelyIdentified = state.MetatypeOptions.Count(candidate =>
                string.Equals(candidate.OptionId, option.OptionId, StringComparison.Ordinal)) == 1;
            bool selected = string.Equals(
                _draft.ConfirmedMetatypeOptionId,
                option.OptionId,
                StringComparison.Ordinal);
            string detail = JoinDetails(
                selected ? "Current selection" : null,
                $"ID {option.OptionId}",
                FormatCosts(option.Costs),
                FormatSource(option.SourceId, option.SourcePage),
                FormatAnchors(option.SourceAnchorIds),
                option.IsEnabled
                    ? null
                    : FormatDisableReason(option.DisableReasonKey, option.DisableReasonArguments),
                uniquelyIdentified ? null : "duplicate-option-id");
            _body.Add(NativeTheme.NavigationRow(
                option.Label,
                detail,
                () => Navigation.PushAsync(new CreationMetatypePreviewPage(
                    Coordinator,
                    _draft,
                    option.OptionId)),
                option.IsEnabled
                && string.IsNullOrWhiteSpace(option.DisableReasonKey)
                && uniquelyIdentified
                && !string.IsNullOrWhiteSpace(option.OptionId),
                $"creation-metatype-option-{Token(option.OptionId)}"));
        }
    }

    private void AddBudget(CharacterCreationBudgetState budget)
    {
        VerticalStackLayout card = new() { Spacing = 7 };
        card.Add(NativeTheme.Eyebrow("Current authoritative budget"));
        card.Add(NativeTheme.Metric("Budget", budget.Label));
        card.Add(NativeTheme.Metric("Budget ID", budget.BudgetId));
        card.Add(NativeTheme.Metric("Total", FormatBudget(budget.Total, budget.Unit)));
        card.Add(NativeTheme.Metric("Used", FormatBudget(budget.Used, budget.Unit)));
        card.Add(NativeTheme.Metric("Remaining", FormatBudget(budget.Remaining, budget.Unit)));
        card.Add(NativeTheme.Body(
            budget.IsExact ? "Exact" : "Not exact",
            budget.IsExact ? NativeTheme.Muted : NativeTheme.Danger));
        foreach (string blocker in budget.Blockers)
            card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-metatype-budget";
        _body.Add(border);
    }

    private void AddBlockers(string title, IReadOnlyList<string> blockers)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(title));
        foreach (string blocker in blockers.Where(static value => !string.IsNullOrWhiteSpace(value)))
            card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-metatype-blockers";
        _body.Add(border);
    }

    private static string FormatCosts(IReadOnlyList<CharacterCreationChoiceCost> costs)
        => costs.Count == 0
            ? "No projected cost"
            : string.Join(
                " · ",
                costs.Select(cost =>
                    $"{cost.BudgetId}: {cost.Delta.ToString("0.##", CultureInfo.InvariantCulture)} {cost.Unit}".TrimEnd()));

    private static string FormatBudget(decimal value, string unit)
        => $"{value.ToString("0.##", CultureInfo.InvariantCulture)} {unit}".TrimEnd();

    private static string? FormatSource(string? source, int? page)
    {
        if (string.IsNullOrWhiteSpace(source))
            return null;
        return page is null ? source : $"{source} p. {page.Value.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string? FormatAnchors(IReadOnlyList<string> anchors)
        => anchors.Count == 0 ? null : $"Anchors {string.Join(" · ", anchors)}";

    private static string FormatDisableReason(
        string? key,
        IReadOnlyDictionary<string, string> arguments)
    {
        string reason = string.IsNullOrWhiteSpace(key) ? "disabled" : key;
        return arguments.Count == 0
            ? reason
            : $"{reason} ({string.Join(", ", arguments.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => $"{item.Key}={item.Value}"))})";
    }

    private static string JoinDetails(params string?[] parts)
        => string.Join(
            " · ",
            parts.Where(static part => !string.IsNullOrWhiteSpace(part)).Select(static part => part!));

    private static string ShortDigest(string digest)
        => string.IsNullOrWhiteSpace(digest) ? "unavailable" : digest[..Math.Min(12, digest.Length)];

    private static string Token(string value)
        => new(value.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());
}
