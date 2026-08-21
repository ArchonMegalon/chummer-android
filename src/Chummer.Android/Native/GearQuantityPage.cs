using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class GearQuantityPage : NativePageBase
{
    private readonly CharacterWorkspaceId _workspaceId;
    private readonly long _contentRevision;
    private readonly WorkspaceGearQuantityLifecycleState _state;
    private readonly Entry _amount;
    private readonly Picker _mergeTarget;
    private readonly Button _increase;
    private readonly Button _reduce;
    private readonly Button _split;
    private readonly Button _merge;

    public GearQuantityPage(
        RunnerSessionCoordinator coordinator,
        CharacterWorkspaceId workspaceId,
        long contentRevision,
        string gearName,
        WorkspaceGearQuantityLifecycleState state) : base(coordinator)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.GearId == Guid.Empty)
        {
            throw new ArgumentException("Gear quantity editing requires a stable Gear Guid.", nameof(state));
        }

        _workspaceId = workspaceId;
        _contentRevision = contentRevision;
        _state = state;
        string targetToken = state.GearId.ToString("N");
        Title = "Gear Quantity";
        AutomationId = $"gear-quantity-page-{targetToken}";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Selected Gear stack"));
        body.Add(NativeTheme.Title(string.IsNullOrWhiteSpace(gearName) ? "Gear Quantity" : gearName));
        Label currentQuantity = NativeTheme.Body(
            $"Current quantity: {state.Quantity.ToString(QuantityFormat(state.DecimalPlaces), CultureInfo.InvariantCulture)}. "
            + $"Exact increment: {state.MinimumIncrement.ToString(CultureInfo.InvariantCulture)}.",
            NativeTheme.Muted);
        currentQuantity.AutomationId = $"gear-quantity-current-{targetToken}";
        body.Add(currentQuantity);

        _amount = new Entry
        {
            Text = state.MinimumIncrement.ToString(CultureInfo.InvariantCulture),
            Keyboard = Keyboard.Numeric,
            AutomationId = $"gear-quantity-amount-{targetToken}",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        body.Add(NativeTheme.FieldLabel("Amount"));
        body.Add(_amount);

        _mergeTarget = new Picker
        {
            Title = "Merge into",
            AutomationId = $"gear-quantity-merge-target-{targetToken}",
            ItemsSource = state.MergeCandidates
                .Select(candidate => $"{candidate.Label} · {candidate.Quantity.ToString(QuantityFormat(state.DecimalPlaces), CultureInfo.InvariantCulture)}")
                .ToArray(),
            SelectedIndex = state.MergeCandidates.Count > 0 ? 0 : -1,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        body.Add(NativeTheme.FieldLabel("Merge target"));
        body.Add(_mergeTarget);

        if (state.PurchaseUnitCostExact)
        {
            body.Add(NativeTheme.Body(
                $"Increase purchases this saved stack at {state.PurchaseUnitCost.ToString(CultureInfo.InvariantCulture)} Nuyen per unit and records an undoable Career expense.",
                NativeTheme.Muted));
        }
        else
        {
            body.Add(NativeTheme.Body(
                "Increase is unavailable because the exact saved purchase cost could not be proven.",
                NativeTheme.Danger));
        }

        _increase = ActionButton("Increase quantity", $"gear-quantity-increase-{targetToken}", GearQuantityAction.Increase);
        _reduce = ActionButton("Reduce quantity", $"gear-quantity-reduce-{targetToken}", GearQuantityAction.Reduce);
        _split = ActionButton("Split stack", $"gear-quantity-split-{targetToken}", GearQuantityAction.Split);
        _merge = ActionButton("Merge stacks", $"gear-quantity-merge-{targetToken}", GearQuantityAction.Merge);
        body.Add(_increase);
        body.Add(_reduce);
        body.Add(_split);
        body.Add(_merge);
        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private Button ActionButton(string label, string automationId, GearQuantityAction action)
    {
        Button button = NativeTheme.PrimaryButton(label);
        button.AutomationId = automationId;
        button.Clicked += async (_, _) => await RunAsync(() => ApplyAsync(action));
        return button;
    }

    private void RefreshEnabledState()
    {
        bool revisionMatches = Coordinator.State.WorkspaceId == _workspaceId
            && Coordinator.State.ContentRevision == _contentRevision;
        _amount.IsEnabled = revisionMatches;
        _mergeTarget.IsEnabled = revisionMatches && _state.MergeCandidates.Count > 0;
        _increase.IsEnabled = revisionMatches && _state.PurchaseUnitCostExact;
        _reduce.IsEnabled = revisionMatches;
        _split.IsEnabled = revisionMatches && _state.Quantity > _state.MinimumIncrement;
        _merge.IsEnabled = revisionMatches && _state.MergeCandidates.Count > 0;
    }

    private async Task ApplyAsync(GearQuantityAction action)
    {
        if (!decimal.TryParse(
                _amount.Text,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimal amount)
            || !CharacterGearQuantityRules.IsValidAmount(amount, _state.MinimumIncrement))
        {
            await DisplayAlertAsync(
                "Invalid amount",
                $"Enter an amount from {_state.MinimumIncrement.ToString(CultureInfo.InvariantCulture)} to {CharacterGearQuantityRules.MaximumQuantity.ToString(CultureInfo.InvariantCulture)} using exact {_state.MinimumIncrement.ToString(CultureInfo.InvariantCulture)} increments.",
                "OK");
            return;
        }

        Guid? mergeTarget = null;
        if (action == GearQuantityAction.Merge)
        {
            int index = _mergeTarget.SelectedIndex;
            if (index < 0 || index >= _state.MergeCandidates.Count)
            {
                await DisplayAlertAsync("Merge target required", "Choose an exact matching Gear stack.", "OK");
                return;
            }
            mergeTarget = _state.MergeCandidates[index].GearId;
        }

        bool reductionConfirmed = false;
        if (action == GearQuantityAction.Reduce)
        {
            reductionConfirmed = await DisplayAlertAsync(
                "Confirm quantity reduction",
                amount >= _state.Quantity
                    ? "This removes the selected Gear stack. Continue?"
                    : $"Reduce the selected Gear stack by {amount.ToString(CultureInfo.InvariantCulture)}?",
                "Reduce",
                "Cancel");
            if (!reductionConfirmed)
            {
                return;
            }
        }

        await Coordinator.ApplyGearQuantityEditAsync(new GearQuantityEditRequest(
            _workspaceId,
            _contentRevision,
            _state.GearId,
            action,
            amount,
            mergeTarget,
            reductionConfirmed));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }

    private static string QuantityFormat(int decimalPlaces)
        => decimalPlaces == 0 ? "0" : $"0.{new string('#', decimalPlaces)}";
}
