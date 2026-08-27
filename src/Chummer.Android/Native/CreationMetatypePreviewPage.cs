using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// Exact, non-writing preview of one authoritative metatype option. The explicit action only
/// confirms its typed identity in the phone navigation draft; Core remains the Foundation rules
/// and persistence authority.
/// </summary>
public sealed class CreationMetatypePreviewPage : NativePageBase
{
    private readonly CreationFoundationPhoneDraft _draft;
    private readonly string _candidateOptionId;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private string? _localBlocker;

    internal CreationMetatypePreviewPage(
        RunnerSessionCoordinator coordinator,
        CreationFoundationPhoneDraft draft,
        string candidateOptionId) : base(coordinator)
    {
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        _candidateOptionId = string.IsNullOrWhiteSpace(candidateOptionId)
            ? throw new ArgumentException("A typed metatype option ID is required.", nameof(candidateOptionId))
            : candidateOptionId;
        Title = CreationAllocationStrings.Get("MetatypePreview.PageTitle", "Review Metatype");
        AutomationId = "creation-metatype-preview-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "MetatypePreview.Eyebrow",
            "Explicit selection review")));
        _body.Add(NativeTheme.Title(CreationAllocationStrings.Get(
            "MetatypePreview.Heading",
            "Review metatype")));

        CharacterCreationFoundationInteractionLoadResult load = Coordinator.LoadCreationFoundation();
        if (Coordinator.State.Profile?.Created != false
            || !string.Equals(load.Outcome, CharacterCreationFoundationOutcomes.Success, StringComparison.Ordinal)
            || load.State is not { } state)
        {
            AddBlockers(load.Blockers.Count > 0 ? load.Blockers : [load.Outcome]);
            return;
        }

        if (!_draft.Matches(state))
        {
            AddBlockers(
                [CreationAllocationStrings.Get(
                    "MetatypePreview.Stale",
                    "The Foundation workspace, revision, or digest changed. Return and reload before confirming.")]);
            return;
        }

        CharacterCreationLegalOption? option = _draft.ResolveCandidate(state, _candidateOptionId);
        if (option is null)
        {
            AddBlockers([CreationAllocationStrings.Get(
                "MetatypePreview.OptionMissing",
                "The typed metatype option is missing or no longer uniquely identified.")]);
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
        binding.AutomationId = "creation-metatype-preview-binding";
        _body.Add(binding);

        AddSelection(option);
        AddBudget(state.LifeModuleBudget);
        AddConsequences(option.Consequences);

        string[] blockers = state.AuthorityBlockers
            .Concat(state.LifeModuleBudget.Blockers)
            .Concat(option.IsEnabled
                ? []
                : [FormatDisableReason(option.DisableReasonKey, option.DisableReasonArguments)])
            .Concat(string.IsNullOrWhiteSpace(option.DisableReasonKey)
                ? []
                : [FormatDisableReason(option.DisableReasonKey, option.DisableReasonArguments)])
            .Append(state.LifeModuleBudget.IsExact
                ? null
                : CreationAllocationStrings.Get(
                    "MetatypePreview.CurrentBudgetNotExact",
                    "The current budget is not exact."))
            .Append(_localBlocker)
            .Where(static blocker => !string.IsNullOrWhiteSpace(blocker))
            .Select(static blocker => blocker!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (blockers.Length > 0)
            AddBlockers(blockers);

        bool canConfirm = option.IsEnabled
                          && string.IsNullOrWhiteSpace(option.DisableReasonKey)
                          && state.AuthorityBlockers.Count == 0
                          && state.LifeModuleBudget.IsExact
                          && state.LifeModuleBudget.Blockers.Count == 0;
        Button confirm = NativeTheme.PrimaryButton(CreationAllocationStrings.Get(
            "MetatypePreview.Confirm",
            "Use this metatype"));
        confirm.AutomationId = "creation-metatype-confirm";
        confirm.IsEnabled = canConfirm;
        confirm.Clicked += async (_, _) => await ConfirmSelectionAsync();
        _body.Add(confirm);

        Label scope = NativeTheme.Body(
            CreationAllocationStrings.Get(
                "MetatypePreview.ConfirmationScope",
                "This confirms only the typed metatype selection for the phone wizard. No character data is written here; the Foundation preview remains the rules authority."),
            NativeTheme.Muted);
        scope.AutomationId = "creation-metatype-confirmation-scope";
        _body.Add(scope);
    }

    private void AddSelection(CharacterCreationLegalOption option)
    {
        VerticalStackLayout card = new() { Spacing = 7 };
        card.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "MetatypePreview.ExactOption",
            "Exact option")));
        card.Add(NativeTheme.Title(option.Label, 22));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("MetatypePreview.OptionId", "Option ID"),
            option.OptionId));
        card.Add(NativeTheme.Metric(
            CreationAllocationStrings.Get("MetatypePreview.Status", "Status"),
            option.IsEnabled
                ? CreationAllocationStrings.Get("Common.Enabled", "enabled")
                : CreationAllocationStrings.Get("Common.Disabled", "disabled")));
        if (!string.IsNullOrWhiteSpace(option.SourceId))
        {
            card.Add(NativeTheme.Metric(
                CreationAllocationStrings.Get("Common.Source", "Source"),
                option.SourcePage is null
                    ? option.SourceId
                    : CreationAllocationStrings.Format(
                        "Common.SourcePage",
                        "{0} p. {1}",
                        option.SourceId,
                        option.SourcePage.Value.ToString(CultureInfo.InvariantCulture))));
        }
        foreach (CharacterCreationChoiceCost cost in option.Costs)
        {
            card.Add(NativeTheme.Metric(
                CreationAllocationStrings.Format(
                    "MetatypePreview.CostBudget",
                    "Cost · {0}",
                    cost.BudgetId),
                FormatBudget(cost.Delta, cost.Unit)));
        }
        if (option.Costs.Count == 0)
            card.Add(NativeTheme.Metric(
                CreationAllocationStrings.Get("MetatypePreview.ProjectedCost", "Projected cost"),
                CreationAllocationStrings.Get("Common.None", "none")));
        foreach (string anchor in option.SourceAnchorIds)
            card.Add(NativeTheme.Body(CreationAllocationStrings.Format(
                "Common.SourceAnchor",
                "Source anchor · {0}",
                anchor), NativeTheme.Muted));
        if (option.SourceAnchorIds.Count == 0)
            card.Add(NativeTheme.Body(CreationAllocationStrings.Get(
                "Common.NoSourceAnchors",
                "No source anchors were projected."), NativeTheme.Muted));
        if (!option.IsEnabled || !string.IsNullOrWhiteSpace(option.DisableReasonKey))
        {
            card.Add(NativeTheme.Body(
                FormatDisableReason(option.DisableReasonKey, option.DisableReasonArguments),
                NativeTheme.Danger));
        }
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-metatype-preview-selection";
        _body.Add(border);
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
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-metatype-preview-budget";
        _body.Add(border);
    }

    private void AddConsequences(IReadOnlyList<CharacterCreationChoiceConsequence> consequences)
    {
        if (consequences.Count == 0)
            return;
        _body.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get(
            "MetatypePreview.ProjectedConsequences",
            "Projected consequences")));
        foreach (CharacterCreationChoiceConsequence consequence in consequences)
        {
            VerticalStackLayout card = new() { Spacing = 6 };
            card.Add(NativeTheme.Metric(
                CreationAllocationStrings.Get("MetatypePreview.ConsequenceId", "Consequence ID"),
                consequence.ConsequenceId));
            card.Add(NativeTheme.Metric(
                CreationAllocationStrings.Get("MetatypePreview.Domain", "Domain"),
                consequence.Domain));
            card.Add(NativeTheme.Metric(
                CreationAllocationStrings.Get("MetatypePreview.Target", "Target"),
                consequence.TargetId));
            card.Add(NativeTheme.Metric(
                CreationAllocationStrings.Get("Common.Before", "Before"),
                consequence.BeforeValue ?? "—"));
            card.Add(NativeTheme.Metric(
                CreationAllocationStrings.Get("Common.After", "After"),
                consequence.AfterValue ?? "—"));
            foreach (string anchor in consequence.SourceAnchorIds)
                card.Add(NativeTheme.Body(CreationAllocationStrings.Format(
                    "Common.SourceAnchor",
                    "Source anchor · {0}",
                    anchor), NativeTheme.Muted));
            Border border = NativeTheme.Card(card, new Thickness(14));
            border.AutomationId = $"creation-metatype-consequence-{Token(consequence.ConsequenceId)}";
            _body.Add(border);
        }
    }

    private void AddBlockers(IReadOnlyList<string> blockers)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get("Common.Blockers", "Blockers")));
        foreach (string blocker in blockers.Where(static value => !string.IsNullOrWhiteSpace(value)))
            card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-metatype-preview-blockers";
        _body.Add(border);
    }

    private async Task ConfirmSelectionAsync()
    {
        _localBlocker = null;
        CharacterCreationFoundationInteractionLoadResult load = Coordinator.LoadCreationFoundation();
        if (Coordinator.State.Profile?.Created != false
            || !string.Equals(load.Outcome, CharacterCreationFoundationOutcomes.Success, StringComparison.Ordinal)
            || load.State is not { } state
            || !_draft.Matches(state)
            || !_draft.TryConfirmMetatype(state, _candidateOptionId))
        {
            _localBlocker = CreationAllocationStrings.Get(
                "MetatypePreview.ConfirmationFailed",
                "The exact metatype selection could not be confirmed.");
            Refresh();
            return;
        }

        await Navigation.PopAsync(animated: false);
        if (Navigation.NavigationStack.LastOrDefault() is CreationMetatypePage)
            await Navigation.PopAsync();
    }

    private static string FormatBudget(decimal value, string unit)
        => $"{value.ToString("0.##", CultureInfo.InvariantCulture)} {unit}".TrimEnd();

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

    private static string ShortDigest(string digest)
        => string.IsNullOrWhiteSpace(digest)
            ? CreationAllocationStrings.Get("Common.Unavailable", "unavailable")
            : digest[..Math.Min(12, digest.Length)];

    private static string Token(string value)
        => new(value.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());
}
