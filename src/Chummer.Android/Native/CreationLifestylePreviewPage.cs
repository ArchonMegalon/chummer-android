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
    private readonly CharacterOverviewState _original;
    private bool _submitted;
    private bool _saving;
    private bool _nextUnavailable;
    private Button? _confirm;
    private CancellationTokenSource? _forwardLifetime;
    private CharacterCreationLifestylesInteractionLoadResult? _loaded;
    private VerticalStackLayout _technicalDetails = new() { Spacing = 6 };

    internal CreationLifestylePreviewPage(
        RunnerSessionCoordinator coordinator,
        CharacterCreationLifestylePreparedPreview prepared) : base(coordinator)
    {
        _prepared = prepared ?? throw new ArgumentNullException(nameof(prepared));
        _original = coordinator.State;
        Title = Copy("Review", "Review lifestyle change");
        AutomationId = "creation-lifestyle-preview-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void OnDisappearing()
    {
        _forwardLifetime?.Cancel();
        base.OnDisappearing();
    }

    protected override async Task PrepareForAppearanceRefreshAsync(CancellationToken cancellationToken)
    {
        _loaded = null;
        Refresh();
        if (_confirmation?.Receipt is not null) return;
        var loaded = await Coordinator.LoadCreationLifestylesForDisplayAsync(Coordinator.State, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _loaded = loaded;
    }

    protected override void Refresh()
    {
        if (_saving) return;
        _body.Clear();
        _technicalDetails = new() { Spacing = 6 };
        _body.Add(NativeTheme.Title(Copy("Review", "Review lifestyle change")));
        AddBinding();
        AddTargetDiff();
        AddBudgets();
        AddWritePlan();
        AddBlockers();
        AddConfirmation();
        AddReceipt();
        var original = Coordinator.State;
        long appearance = CaptureAppearanceGeneration();
        _body.Add(NativeTheme.TechnicalDetails(_technicalDetails, "creation-lifestyle-preview-details",
            () => IsCurrentAppearanceGeneration(appearance)
                && Coordinator.IsCreationCatalogDisplayCurrent(original)
                && original.DisplayOwnerContext == _prepared.DisplayOwnerContext));
    }

    private void AddBinding()
    {
        Label binding = NativeTheme.Body(
            $"Revision {_prepared.Binding.ContentRevision} · saved {_prepared.Binding.SavedRevision} · "
            + $"{RunnerSessionCoordinator.HumanizeId(_prepared.Mutation.MutationKind)} "
            + _prepared.Mutation.LifestyleId.ToString("D"),
            NativeTheme.Muted);
        binding.AutomationId = "creation-lifestyle-preview-binding";
        _technicalDetails.Add(binding);
        _technicalDetails.Add(DigestLabel("Preview digest", _prepared.PreviewDigest, "creation-lifestyle-preview-digest"));
        _technicalDetails.Add(DigestLabel("Atomic plan digest", _prepared.WritePlan.PlanDigest, "creation-lifestyle-plan-digest"));
    }

    private void AddTargetDiff()
    {
        VerticalStackLayout card = new() { Spacing = 7 };
        card.Add(NativeTheme.Eyebrow(Copy("Changes", "Changes")));
        card.Add(NativeTheme.Metric(Copy("Mutation","Mutation"), RunnerSessionCoordinator.HumanizeId(_prepared.Mutation.MutationKind)));
        card.Add(NativeTheme.Metric(Copy("Name","Name"), $"{Name(_prepared.Before)} → {Name(_prepared.After)}"));
        card.Add(NativeTheme.Metric(Copy("Base","Base"), $"{Base(_prepared.Before)} → {Base(_prepared.After)}"));
        card.Add(NativeTheme.Metric(Copy("Perincrement","Per increment"), $"{Cost(_prepared.Before)} → {Cost(_prepared.After)}"));
        card.Add(NativeTheme.Metric(Copy("Total","Total"), $"{Total(_prepared.Before)} → {Total(_prepared.After)}"));
        card.Add(NativeTheme.Metric(Copy("Lifestylepoints","Lifestyle points"), $"{Points(_prepared.Before)} → {Points(_prepared.After)}"));
        card.Add(NativeTheme.Metric(Copy("Qualities","Qualities"), $"{QualityCount(_prepared.Before)} → {QualityCount(_prepared.After)}"));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-lifestyle-preview-target";
        _body.Add(border);
    }

    private void AddBudgets()
    {
        AddBudget(Copy("BudgetBefore", "Starting nuyen before"), _prepared.BudgetBefore, "creation-lifestyle-preview-budget-before");
        AddBudget(Copy("BudgetAfter", "Starting nuyen after"), _prepared.BudgetAfter, "creation-lifestyle-preview-budget-after");
    }

    private void AddBudget(string title, CharacterCreationLifestyleBudget budget, string automationId)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(title));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.Total", "Total"), budget.Total.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.Used", "Used"), budget.Used.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.Remaining", "Remaining"), budget.Remaining.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(Copy("Overspend", "Overspend"), budget.Overspend.ToString(CultureInfo.InvariantCulture)));
        Border border = NativeTheme.Card(card);
        border.AutomationId = automationId;
        SemanticProperties.SetDescription(
            border,
            $"{title}. Used {budget.Used}. Remaining {budget.Remaining}. Overspend {budget.Overspend}.");
        _body.Add(border);
    }

    private void AddWritePlan()
    {
        _technicalDetails.Add(NativeTheme.Eyebrow("Ordered atomic write plan"));
        foreach (CharacterCreationLifestyleWriteOperation operation in _prepared.WritePlan.Operations)
        {
            VerticalStackLayout operationCard = new() { Spacing = 6 };
            operationCard.Add(NativeTheme.Title(
                $"{operation.Order}. {RunnerSessionCoordinator.HumanizeId(operation.MutationKind)}",
                18));
            operationCard.Add(NativeTheme.Metric("Before digest", operation.BeforeDigest));
            operationCard.Add(NativeTheme.Metric("After digest", operation.AfterDigest));
            operationCard.Add(NativeTheme.Body(
                $"Source · {string.Join(" · ", operation.SourceAnchorIds)}",
                NativeTheme.Muted));
            Border border = NativeTheme.Card(operationCard, new Thickness(14));
            border.AutomationId = $"creation-lifestyle-write-{operation.Order}-{operation.MutationKind}";
            _technicalDetails.Add(border);
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
        _technicalDetails.Add(card);
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
        card.Add(NativeTheme.Eyebrow(Copy("NeedsAttention", "Needs attention")));
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
                Copy("Saved", "Lifestyle change saved."));
            done.AutomationId = "creation-lifestyle-confirmed";
            _body.Add(NativeTheme.Card(done));
            return;
        }

        if (_confirmation?.Receipt is { } committed
            && CreationLifestylesPhoneAuthority.ReceiptMatches(_prepared, committed))
        {
            Label saved = NativeTheme.Body(
                Copy("SavedReload", "Your change is saved. Reopen the runner in its original account to continue. Do not submit the change again."));
            saved.AutomationId = "creation-lifestyle-committed-reload-required";
            _body.Add(NativeTheme.Card(saved));
            return;
        }

        if (_submitted)
        {
            _body.Add(NativeTheme.Body(Copy("OutcomeUnknown",
                "The result could not be verified. Reopen this runner before making another change."), NativeTheme.Muted));
            return;
        }

        CheckBox explicitConfirm = new()
        {
            AutomationId = "creation-lifestyle-explicit-confirm",
            IsChecked = _explicitlyConfirmed,
            Color = NativeTheme.Signal
        };
        Label explicitLabel = NativeTheme.Body(
            Copy("ConfirmHelp", "I have reviewed these details and costs and want to save this change."));
        Grid explicitRow = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 10
        };
        explicitRow.Add(explicitConfirm);
        explicitRow.Add(explicitLabel, 1);
        _body.Add(NativeTheme.Card(explicitRow, new Thickness(14)));

        Button confirm = NativeTheme.PrimaryButton(Copy("Confirm", "Save lifestyle change"));
        confirm.AutomationId = "creation-lifestyle-confirm";
        confirm.IsEnabled = CanConfirm() && _explicitlyConfirmed;
        explicitConfirm.CheckedChanged += (_, args) =>
        {
            _explicitlyConfirmed = args.Value;
            confirm.IsEnabled = CanConfirm() && _explicitlyConfirmed;
        };
        confirm.Clicked += async (_, _) => await RunAsync(async () =>
        {
            long appearance = CaptureAppearanceGeneration();
            if (_submitted || !_explicitlyConfirmed || !CanConfirm()
                || !IsCurrentAppearanceGeneration(appearance)
                || !ReferenceEquals(Navigation.NavigationStack.LastOrDefault(), this)
                || !Coordinator.IsCreationCatalogDisplayCurrent(_original)) return;
            _submitted = true;
            _saving = true;
            confirm.IsEnabled = false;
            confirm.Text = Copy("Saving", "Saving lifestyle…");
            try
            {
                _confirmation = await Coordinator.ConfirmCreationLifestyleAsync(_prepared);
                if (_confirmation.RefreshedState is not null) await ContinueToLifestylesAsync(appearance);
            }
            finally { _saving = false; }
        });
        _confirm = confirm;
        _body.Add(confirm);
    }

    private bool CanContinueSaved() => _confirmation?.Receipt is { } receipt
        && CreationLifestylesPhoneAuthority.ReceiptMatches(_prepared, receipt)
        && _original.DisplayOwnerContext == _prepared.DisplayOwnerContext
        && Coordinator.CanDisplayCreationPurchase(_original) && Coordinator.State.Profile?.Created == false;

    private async Task ContinueToLifestylesAsync(long appearance)
    {
        if (_forwardLifetime is not null || !CanContinueSaved()) return;
        var navigation = Navigation;
        var stack = navigation.NavigationStack.ToArray();
        bool Current() => IsCurrentAppearanceGeneration(appearance) && CanContinueSaved()
            && ReferenceEquals(stack.LastOrDefault(), this) && navigation.NavigationStack.SequenceEqual(stack);
        if (!Current()) return;
        using var lifetime = new CancellationTokenSource();
        _forwardLifetime = lifetime;
        _nextUnavailable = false;
        if (_confirm is { } button)
        {
            button.IsEnabled = false;
            button.Text = Copy("OpeningList", "Opening lifestyles…");
        }
        try
        {
            var page = await Coordinator.PrepareCreationAfterLifestylePageAsync(_original, _prepared,
                _confirmation!.Receipt!, _confirmation.RefreshedState, lifetime.Token);
            if (!Current() || lifetime.IsCancellationRequested) return;
            if (page is null || !page.IsPreparedDisplayCurrent) { _nextUnavailable = true; return; }
            await navigation.PushAsync(page, animated: false);
            if (!navigation.NavigationStack.SequenceEqual(stack.Append(page))) return;
            // Retire only this contiguous Lifestyles route. Keep the root and any
            // unrelated page, and never send Back into a consumed mutation preview.
            for (int i = stack.Length - 2; i > 0; i--)
            {
                if (stack[i] is not (CreationLifestyleEditPage or CreationLifestylesPage)) break;
                navigation.RemovePage(stack[i]);
            }
            // Removing this page detaches its NavigationProxy. Remove ancestors
            // first, while that proxy still belongs to the live navigation host.
            navigation.RemovePage(this);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        { if (Current()) _nextUnavailable = true; }
        finally
        {
            if (ReferenceEquals(_forwardLifetime, lifetime)) _forwardLifetime = null;
            if (!_saving) Refresh();
        }
    }

    private bool CanConfirm()
    {
        var live = _loaded;
        return live?.State is { } state
               && live.Blockers.Count == 0
               && _prepared.RequiresExplicitConfirmation
               && _prepared.CanConfirm
               && _prepared.Blockers.Count == 0
               && CreationLifestylesPhoneAuthority.PreparedMatches(
                   _prepared,
                   state,
                   Coordinator.State);
    }

    private void AddReceipt()
    {
        if (_confirmation?.Receipt is not { } receipt
            || !CreationLifestylesPhoneAuthority.ReceiptMatches(_prepared, receipt)) return;
        var card = new VerticalStackLayout { Spacing = 7 };
        card.Add(NativeTheme.Body(Copy("Saved", "Lifestyle change saved.")));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.Remaining", "Remaining"),
            receipt.LifestyleBudgetRemaining.ToString(CultureInfo.InvariantCulture)));
        var border = NativeTheme.Card(card);
        border.AutomationId = "creation-lifestyle-confirm-receipt";
        _body.Add(border);
        _technicalDetails.Add(DigestLabel("Receipt ID", receipt.ReceiptId, "creation-lifestyle-receipt-id"));
        _technicalDetails.Add(DigestLabel("Receipt digest", receipt.ReceiptDigest, "creation-lifestyle-receipt-digest"));
        if (_nextUnavailable) _body.Add(NativeTheme.Body(Copy("ListUnavailable",
            "Your lifestyle is saved. Try Continue again to reopen the list; this will not save twice."), NativeTheme.Muted));
        long appearance = CaptureAppearanceGeneration();
        var next = NativeTheme.PrimaryButton(Copy("ContinueLifestyles", "Continue to lifestyles"));
        next.AutomationId = "creation-lifestyle-continue-lifestyles";
        next.IsEnabled = CanContinueSaved();
        next.Clicked += async (_, _) => await RunAsync(() => ContinueToLifestylesAsync(appearance));
        _confirm = next;
        _body.Add(next);
    }

    private static string Copy(string key, string fallback)
        => CreationFlowStrings.Get("Lifestyles." + key, fallback);

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
