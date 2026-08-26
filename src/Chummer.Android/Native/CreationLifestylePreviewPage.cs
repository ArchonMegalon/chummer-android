using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>Immutable Core preview plus explicit confirmation for one Lifestyle mutation.</summary>
public sealed class CreationLifestylePreviewPage : NativePageBase
{
    private readonly CharacterCreationLifestylePreparedPreview _prepared;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private CreationLifestylePhoneConfirmResult? _confirmation;
    private bool _explicitlyConfirmed;

    internal CreationLifestylePreviewPage(
        RunnerSessionCoordinator coordinator,
        CharacterCreationLifestylePreparedPreview prepared) : base(coordinator)
    {
        _prepared = prepared ?? throw new ArgumentNullException(nameof(prepared));
        Title = "Review lifestyle change";
        AutomationId = "creation-lifestyle-preview-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Explicit review"));
        _body.Add(NativeTheme.Title("Creation Lifestyle change"));
        AddBinding();
        AddTargetDiff();
        AddBudgets();
        AddWritePlan();
        AddBlockers();
        AddConfirmation();
        AddReceipt();
    }

    private void AddBinding()
    {
        Label binding = NativeTheme.Body(
            $"Revision {_prepared.Binding.ContentRevision} · saved {_prepared.Binding.SavedRevision} · "
            + $"{RunnerSessionCoordinator.HumanizeId(_prepared.Mutation.MutationKind)} "
            + _prepared.Mutation.LifestyleId.ToString("D"),
            NativeTheme.Muted);
        binding.AutomationId = "creation-lifestyle-preview-binding";
        _body.Add(binding);
        _body.Add(DigestLabel("Preview digest", _prepared.PreviewDigest, "creation-lifestyle-preview-digest"));
        _body.Add(DigestLabel("Atomic plan digest", _prepared.WritePlan.PlanDigest, "creation-lifestyle-plan-digest"));
    }

    private void AddTargetDiff()
    {
        VerticalStackLayout card = new() { Spacing = 7 };
        card.Add(NativeTheme.Eyebrow("Target before / after"));
        card.Add(NativeTheme.Metric("Mutation", RunnerSessionCoordinator.HumanizeId(_prepared.Mutation.MutationKind)));
        card.Add(NativeTheme.Metric("Name", $"{Name(_prepared.Before)} → {Name(_prepared.After)}"));
        card.Add(NativeTheme.Metric("Base", $"{Base(_prepared.Before)} → {Base(_prepared.After)}"));
        card.Add(NativeTheme.Metric("Per increment", $"{Cost(_prepared.Before)} → {Cost(_prepared.After)}"));
        card.Add(NativeTheme.Metric("Total", $"{Total(_prepared.Before)} → {Total(_prepared.After)}"));
        card.Add(NativeTheme.Metric("Lifestyle points", $"{Points(_prepared.Before)} → {Points(_prepared.After)}"));
        card.Add(NativeTheme.Metric("Qualities", $"{QualityCount(_prepared.Before)} → {QualityCount(_prepared.After)}"));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-lifestyle-preview-target";
        _body.Add(border);
    }

    private void AddBudgets()
    {
        AddBudget("Starting nuyen before", _prepared.BudgetBefore, "creation-lifestyle-preview-budget-before");
        AddBudget("Starting nuyen after", _prepared.BudgetAfter, "creation-lifestyle-preview-budget-after");
    }

    private void AddBudget(string title, CharacterCreationLifestyleBudget budget, string automationId)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(title));
        card.Add(NativeTheme.Metric("Total", budget.Total.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric("Used", budget.Used.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric("Remaining", budget.Remaining.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric("Overspend", budget.Overspend.ToString(CultureInfo.InvariantCulture)));
        Border border = NativeTheme.Card(card);
        border.AutomationId = automationId;
        SemanticProperties.SetDescription(
            border,
            $"{title}. Used {budget.Used}. Remaining {budget.Remaining}. Overspend {budget.Overspend}.");
        _body.Add(border);
    }

    private void AddWritePlan()
    {
        _body.Add(NativeTheme.Eyebrow("Ordered atomic write plan"));
        foreach (CharacterCreationLifestyleWriteOperation operation in _prepared.WritePlan.Operations)
        {
            VerticalStackLayout card = new() { Spacing = 6 };
            card.Add(NativeTheme.Title(
                $"{operation.Order}. {RunnerSessionCoordinator.HumanizeId(operation.MutationKind)}",
                18));
            card.Add(NativeTheme.Metric("Before digest", operation.BeforeDigest));
            card.Add(NativeTheme.Metric("After digest", operation.AfterDigest));
            card.Add(NativeTheme.Body(
                $"Source · {string.Join(" · ", operation.SourceAnchorIds)}",
                NativeTheme.Muted));
            Border border = NativeTheme.Card(card, new Thickness(14));
            border.AutomationId = $"creation-lifestyle-write-{operation.Order}-{operation.MutationKind}";
            _body.Add(border);
        }

        VerticalStackLayout preservation = new() { Spacing = 6 };
        preservation.Add(NativeTheme.Eyebrow("Preservation authority"));
        preservation.Add(NativeTheme.Metric(
            "Untouched siblings",
            _prepared.WritePlan.PreservesUntouchedSiblingState ? "preserved" : "not proven"));
        preservation.Add(NativeTheme.Metric(
            "Nested target state",
            _prepared.WritePlan.PreservesNestedState ? "preserved" : "not proven"));
        preservation.Add(NativeTheme.Metric("Content before", _prepared.WritePlan.ContentDigestBefore));
        preservation.Add(NativeTheme.Metric("Content after", _prepared.WritePlan.ContentDigestAfter));
        Border card = NativeTheme.Card(preservation);
        card.AutomationId = "creation-lifestyle-preview-preservation";
        _body.Add(card);
    }

    private void AddBlockers()
    {
        string[] blockers = _prepared.Blockers
            .Concat(_confirmation?.Blockers ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (blockers.Length == 0)
            return;
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow("Blockers"));
        foreach (string blocker in blockers)
            card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-lifestyle-confirm-blockers";
        _body.Add(border);
    }

    private void AddConfirmation()
    {
        if (_confirmation is
            {
                Outcome: CharacterCreationLifestyleOutcomes.Applied or CharacterCreationLifestyleOutcomes.Replayed,
                Receipt: not null,
                RefreshedState: not null
            })
        {
            Label done = NativeTheme.Body(
                _confirmation.RecoveredByReceiptLookup
                    ? "Confirmed and recovered by the retained idempotency receipt."
                    : "Confirmed, atomically checkpointed, and reloaded from Core.");
            done.AutomationId = "creation-lifestyle-confirmed";
            _body.Add(NativeTheme.Card(done));
            return;
        }

        CheckBox explicitConfirm = new()
        {
            AutomationId = "creation-lifestyle-explicit-confirm",
            IsChecked = _explicitlyConfirmed,
            Color = NativeTheme.Signal
        };
        Label explicitLabel = NativeTheme.Body(
            "I explicitly confirm this exact Lifestyle preview and atomic write plan.");
        Grid row = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 10
        };
        row.Add(explicitConfirm);
        row.Add(explicitLabel, 1);
        _body.Add(NativeTheme.Card(row, new Thickness(14)));

        Button confirm = NativeTheme.PrimaryButton("Confirm Lifestyle change");
        confirm.AutomationId = "creation-lifestyle-confirm";
        confirm.IsEnabled = CanConfirm() && _explicitlyConfirmed;
        explicitConfirm.CheckedChanged += (_, args) =>
        {
            _explicitlyConfirmed = args.Value;
            confirm.IsEnabled = CanConfirm() && _explicitlyConfirmed;
        };
        confirm.Clicked += async (_, _) => await RunAsync(async () =>
        {
            _confirmation = await Coordinator.ConfirmCreationLifestyleAsync(_prepared);
        });
        _body.Add(confirm);
    }

    private bool CanConfirm()
    {
        CharacterCreationLifestylesInteractionLoadResult live = Coordinator.LoadCreationLifestyles();
        return live.State is { } state
               && live.Blockers.Count == 0
               && CreationLifestylesPhoneAuthority.PreparedMatches(
                   _prepared,
                   state,
                   Coordinator.State);
    }

    private void AddReceipt()
    {
        if (_confirmation is not
            {
                Outcome: CharacterCreationLifestyleOutcomes.Applied or CharacterCreationLifestyleOutcomes.Replayed,
                Receipt: { } receipt,
                RefreshedState: { } refreshed
            })
        {
            return;
        }

        VerticalStackLayout card = new() { Spacing = 7 };
        card.Add(NativeTheme.Eyebrow("Atomic creation receipt"));
        card.Add(NativeTheme.Metric("Previous revision", receipt.PreviousContentRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric("Content revision", receipt.ContentRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric("Saved revision", receipt.SavedRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric("Lifestyle cost before", receipt.LifestyleCostBefore.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric("Lifestyle cost after", receipt.LifestyleCostAfter.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric("Remaining", receipt.LifestyleBudgetRemaining.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric("Reloaded Lifestyles", refreshed.Lifestyles.Count.ToString(CultureInfo.InvariantCulture)));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-lifestyle-confirm-receipt";
        _body.Add(border);

        _body.Add(DigestLabel("Receipt ID", receipt.ReceiptId, "creation-lifestyle-receipt-id"));
        _body.Add(DigestLabel("Receipt digest", receipt.ReceiptDigest, "creation-lifestyle-receipt-digest"));
        _body.Add(DigestLabel("Content before", receipt.ContentDigestBefore, "creation-lifestyle-receipt-content-before"));
        _body.Add(DigestLabel("Content after", receipt.ContentDigestAfter, "creation-lifestyle-receipt-content-after"));
        _body.Add(DigestLabel("Source", receipt.SourceDigest, "creation-lifestyle-receipt-source"));
        _body.Add(DigestLabel("Rules", receipt.RulesDigest, "creation-lifestyle-receipt-rules"));
        _body.Add(DigestLabel("Runtime", receipt.RuntimeDigest, "creation-lifestyle-receipt-runtime"));

        Button back = NativeTheme.PrimaryButton("Back to creation dashboard");
        back.AutomationId = "creation-lifestyle-back-to-build";
        back.Clicked += async (_, _) => await Navigation.PopToRootAsync(animated: false);
        _body.Add(back);
    }

    private Label DigestLabel(string label, string value, string automationId)
    {
        Label text = NativeTheme.Body($"{label} · {value}", NativeTheme.Muted);
        text.AutomationId = automationId;
        return text;
    }

    private static string Name(CharacterCreationLifestyleProjection? value)
        => value?.Configuration.Name ?? "none";

    private static string Base(CharacterCreationLifestyleProjection? value)
        => value?.BaseLifestyleName ?? "none";

    private static string Cost(CharacterCreationLifestyleProjection? value)
        => value?.Economics.CostPerIncrement.ToString(CultureInfo.InvariantCulture) ?? "0";

    private static string Total(CharacterCreationLifestyleProjection? value)
        => value?.Economics.TotalCost.ToString(CultureInfo.InvariantCulture) ?? "0";

    private static string Points(CharacterCreationLifestyleProjection? value)
        => value?.Economics.LifestylePointsRemaining.ToString(CultureInfo.InvariantCulture) ?? "0";

    private static string QualityCount(CharacterCreationLifestyleProjection? value)
        => value?.Configuration.Qualities.Count.ToString(CultureInfo.InvariantCulture) ?? "0";
}
