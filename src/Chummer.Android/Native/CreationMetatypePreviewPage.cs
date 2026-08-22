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
        Title = "Review Metatype";
        AutomationId = "creation-metatype-preview-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Explicit selection review"));
        _body.Add(NativeTheme.Title("Review metatype"));

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
                ["The Foundation workspace, revision, or digest changed. Return and reload before confirming."]);
            return;
        }

        CharacterCreationLegalOption? option = _draft.ResolveCandidate(state, _candidateOptionId);
        if (option is null)
        {
            AddBlockers(["The typed metatype option is missing or no longer uniquely identified."]);
            return;
        }

        Label binding = NativeTheme.Body(
            $"Revision {state.Binding.ContentRevision} · saved {state.Binding.SavedRevision} · "
            + $"snapshot {ShortDigest(state.FoundationSnapshotDigest)}",
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
            .Append(state.LifeModuleBudget.IsExact ? null : "The current budget is not exact.")
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
        Button confirm = NativeTheme.PrimaryButton("Use this metatype");
        confirm.AutomationId = "creation-metatype-confirm";
        confirm.IsEnabled = canConfirm;
        confirm.Clicked += async (_, _) => await ConfirmSelectionAsync();
        _body.Add(confirm);

        Label scope = NativeTheme.Body(
            "This confirms only the typed metatype selection for the phone wizard. "
            + "No character data is written here; the Foundation preview remains the rules authority.",
            NativeTheme.Muted);
        scope.AutomationId = "creation-metatype-confirmation-scope";
        _body.Add(scope);
    }

    private void AddSelection(CharacterCreationLegalOption option)
    {
        VerticalStackLayout card = new() { Spacing = 7 };
        card.Add(NativeTheme.Eyebrow("Exact option"));
        card.Add(NativeTheme.Title(option.Label, 22));
        card.Add(NativeTheme.Metric("Option ID", option.OptionId));
        card.Add(NativeTheme.Metric("Status", option.IsEnabled ? "enabled" : "disabled"));
        if (!string.IsNullOrWhiteSpace(option.SourceId))
        {
            card.Add(NativeTheme.Metric(
                "Source",
                option.SourcePage is null
                    ? option.SourceId
                    : $"{option.SourceId} p. {option.SourcePage.Value.ToString(CultureInfo.InvariantCulture)}"));
        }
        foreach (CharacterCreationChoiceCost cost in option.Costs)
        {
            card.Add(NativeTheme.Metric(
                $"Cost · {cost.BudgetId}",
                FormatBudget(cost.Delta, cost.Unit)));
        }
        if (option.Costs.Count == 0)
            card.Add(NativeTheme.Metric("Projected cost", "none"));
        foreach (string anchor in option.SourceAnchorIds)
            card.Add(NativeTheme.Body($"Source anchor · {anchor}", NativeTheme.Muted));
        if (option.SourceAnchorIds.Count == 0)
            card.Add(NativeTheme.Body("No source anchors were projected.", NativeTheme.Muted));
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
        card.Add(NativeTheme.Eyebrow("Current authoritative budget"));
        card.Add(NativeTheme.Metric("Budget", budget.Label));
        card.Add(NativeTheme.Metric("Budget ID", budget.BudgetId));
        card.Add(NativeTheme.Metric("Total", FormatBudget(budget.Total, budget.Unit)));
        card.Add(NativeTheme.Metric("Used", FormatBudget(budget.Used, budget.Unit)));
        card.Add(NativeTheme.Metric("Remaining", FormatBudget(budget.Remaining, budget.Unit)));
        card.Add(NativeTheme.Body(
            budget.IsExact ? "Exact" : "Not exact",
            budget.IsExact ? NativeTheme.Muted : NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-metatype-preview-budget";
        _body.Add(border);
    }

    private void AddConsequences(IReadOnlyList<CharacterCreationChoiceConsequence> consequences)
    {
        if (consequences.Count == 0)
            return;
        _body.Add(NativeTheme.Eyebrow("Projected consequences"));
        foreach (CharacterCreationChoiceConsequence consequence in consequences)
        {
            VerticalStackLayout card = new() { Spacing = 6 };
            card.Add(NativeTheme.Metric("Consequence ID", consequence.ConsequenceId));
            card.Add(NativeTheme.Metric("Domain", consequence.Domain));
            card.Add(NativeTheme.Metric("Target", consequence.TargetId));
            card.Add(NativeTheme.Metric("Before", consequence.BeforeValue ?? "—"));
            card.Add(NativeTheme.Metric("After", consequence.AfterValue ?? "—"));
            foreach (string anchor in consequence.SourceAnchorIds)
                card.Add(NativeTheme.Body($"Source anchor · {anchor}", NativeTheme.Muted));
            Border border = NativeTheme.Card(card, new Thickness(14));
            border.AutomationId = $"creation-metatype-consequence-{Token(consequence.ConsequenceId)}";
            _body.Add(border);
        }
    }

    private void AddBlockers(IReadOnlyList<string> blockers)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow("Blockers"));
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
            _localBlocker = "The exact metatype selection could not be confirmed.";
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
        string reason = string.IsNullOrWhiteSpace(key) ? "disabled" : key;
        return arguments.Count == 0
            ? reason
            : $"{reason} ({string.Join(", ", arguments.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => $"{item.Key}={item.Value}"))})";
    }

    private static string ShortDigest(string digest)
        => string.IsNullOrWhiteSpace(digest) ? "unavailable" : digest[..Math.Min(12, digest.Length)];

    private static string Token(string value)
        => new(value.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());
}
