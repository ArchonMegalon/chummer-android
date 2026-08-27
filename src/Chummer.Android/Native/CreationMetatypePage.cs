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
        Title = CreationAllocationStrings.Get("Metatype.PageTitle", "Metatype");
        AutomationId = "creation-metatype-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "Metatype.Foundation",
            "Foundation")));
        _body.Add(NativeTheme.Title(CreationAllocationStrings.Get(
            "Metatype.Heading",
            "Choose a metatype")));

        CharacterCreationFoundationInteractionLoadResult load = Coordinator.LoadCreationFoundation();
        if (Coordinator.State.Profile?.Created != false
            || !string.Equals(load.Outcome, CharacterCreationFoundationOutcomes.Success, StringComparison.Ordinal)
            || load.State is not { } state)
        {
            AddBlockers(
                CreationAllocationStrings.Get(
                    "Metatype.AuthorityUnavailable",
                    "Metatype authority unavailable"),
                load.Blockers.Count > 0 ? load.Blockers : [load.Outcome]);
            return;
        }

        if (!_draft.Matches(state))
        {
            AddBlockers(
                CreationAllocationStrings.Get(
                    "Metatype.SelectionStale",
                    "Metatype selection is stale"),
                [CreationAllocationStrings.Get(
                    "Metatype.SelectionStaleDetail",
                    "The Foundation workspace, revision, or digest changed. Return and reload before selecting.")]);
            return;
        }

        Label binding = NativeTheme.Body(
            CreationAllocationStrings.Format(
                "Common.SnapshotBinding",
                "Revision {0} · saved {1} · snapshot {2}",
                state.Binding.ContentRevision,
                state.Binding.SavedRevision,
                ShortDigest(state.FoundationSnapshotDigest)),
            NativeTheme.Muted);
        binding.AutomationId = "creation-metatype-binding";
        _body.Add(binding);

        AddBudget(state.LifeModuleBudget);
        if (state.AuthorityBlockers.Count > 0)
            AddBlockers(
                CreationAllocationStrings.Get("Common.AuthorityBlockers", "Authority blockers"),
                state.AuthorityBlockers);

        if (state.MetatypeOptions.Count == 0)
        {
            AddBlockers(
                CreationAllocationStrings.Get("Metatype.NoLegalOptions", "No legal options"),
                [CreationAllocationStrings.Get(
                    "Metatype.NoProjectedOptions",
                    "No metatype options were projected by the authority.")]);
            return;
        }

        _body.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "Metatype.AuthoritativeOptions",
            "Authoritative options")));
        foreach (CharacterCreationLegalOption option in state.MetatypeOptions)
        {
            bool uniquelyIdentified = state.MetatypeOptions.Count(candidate =>
                string.Equals(candidate.OptionId, option.OptionId, StringComparison.Ordinal)) == 1;
            bool selected = string.Equals(
                _draft.ConfirmedMetatypeOptionId,
                option.OptionId,
                StringComparison.Ordinal);
            string detail = JoinDetails(
                selected
                    ? CreationAllocationStrings.Get("Metatype.CurrentSelection", "Current selection")
                    : null,
                CreationAllocationStrings.Format("Common.Id", "ID {0}", option.OptionId),
                FormatCosts(option.Costs),
                FormatSource(option.SourceId, option.SourcePage),
                FormatAnchors(option.SourceAnchorIds),
                option.IsEnabled
                    ? null
                    : FormatDisableReason(option.DisableReasonKey, option.DisableReasonArguments),
                uniquelyIdentified
                    ? null
                    : CreationAllocationStrings.Get(
                        "Metatype.DuplicateOptionId",
                        "Duplicate option ID"));
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
        card.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "Common.CurrentAuthoritativeBudget",
            "Current authoritative budget")));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.Budget", "Budget"),
            budget.Label));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.BudgetId", "Budget ID"),
            budget.BudgetId));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.Total", "Total"),
            FormatBudget(budget.Total, budget.Unit)));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.Used", "Used"),
            FormatBudget(budget.Used, budget.Unit)));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("Common.Remaining", "Remaining"),
            FormatBudget(budget.Remaining, budget.Unit)));
        card.Add(NativeTheme.Body(
            budget.IsExact
                ? CreationAllocationStrings.Get("Common.Exact", "Exact")
                : CreationAllocationStrings.Get("Common.NotExact", "Not exact"),
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
            ? CreationAllocationStrings.Get("Metatype.NoProjectedCost", "No projected cost")
            : string.Join(
                " · ",
                costs.Select(cost =>
                    CreationAllocationStrings.Format(
                        "Metatype.Cost",
                        "{0}: {1} {2}",
                        cost.BudgetId,
                        cost.Delta.ToString("0.##", CultureInfo.InvariantCulture),
                        cost.Unit).TrimEnd()));

    private static string FormatBudget(decimal value, string unit)
        => $"{value.ToString("0.##", CultureInfo.InvariantCulture)} {unit}".TrimEnd();

    private static string? FormatSource(string? source, int? page)
    {
        if (string.IsNullOrWhiteSpace(source))
            return null;
        return page is null
            ? source
            : CreationAllocationStrings.Format(
                "Common.SourcePage",
                "{0} p. {1}",
                source,
                page.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static string? FormatAnchors(IReadOnlyList<string> anchors)
        => anchors.Count == 0
            ? null
            : CreationAllocationStrings.Format(
                "Common.Anchors",
                "Anchors {0}",
                string.Join(" · ", anchors));

    private static string FormatDisableReason(
        string? key,
        IReadOnlyDictionary<string, string> arguments)
    {
        string reason = string.IsNullOrWhiteSpace(key)
            ? CreationAllocationStrings.Get("Common.Disabled", "disabled")
            : key;
        return arguments.Count == 0
            ? reason
            : CreationAllocationStrings.Format(
                "Common.ReasonArguments",
                "{0} ({1})",
                reason,
                string.Join(", ", arguments.OrderBy(item => item.Key, StringComparer.Ordinal)
                    .Select(item => $"{item.Key}={item.Value}")));
    }

    private static string JoinDetails(params string?[] parts)
        => string.Join(
            " · ",
            parts.Where(static part => !string.IsNullOrWhiteSpace(part)).Select(static part => part!));

    private static string ShortDigest(string digest)
        => string.IsNullOrWhiteSpace(digest)
            ? CreationAllocationStrings.Get("Common.Unavailable", "unavailable")
            : digest[..Math.Min(12, digest.Length)];

    private static string Token(string value)
        => new(value.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());
}
