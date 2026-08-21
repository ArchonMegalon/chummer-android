using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class LifestyleIncrementPage : NativePageBase
{
    private readonly CharacterWorkspaceId _workspaceId;
    private readonly long _contentRevision;
    private readonly CharacterLifestyleIncrementState _state;
    private readonly Entry? _creationValue;
    private readonly Button _primary;
    private readonly Button? _decrease;

    public LifestyleIncrementPage(
        RunnerSessionCoordinator coordinator,
        CharacterWorkspaceId workspaceId,
        long contentRevision,
        string lifestyleName,
        CharacterLifestyleIncrementState state) : base(coordinator)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.LifestyleId == Guid.Empty)
        {
            throw new ArgumentException("Lifestyle interval editing requires a stable Lifestyle Guid.", nameof(state));
        }

        _workspaceId = workspaceId;
        _contentRevision = contentRevision;
        _state = state;
        string token = state.LifestyleId.ToString("N");
        string unit = UnitLabel(state.Unit);
        Title = "Lifestyle Intervals";
        AutomationId = $"lifestyle-increments-page-{token}";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow(state.CareerMode ? "Career Lifestyle" : "Creation Lifestyle"));
        body.Add(NativeTheme.Title(string.IsNullOrWhiteSpace(lifestyleName) ? state.DisplayName : lifestyleName));
        Label current = NativeTheme.Body(
            $"Current {unit.ToLowerInvariant()} intervals: {state.Increments.ToString(CultureInfo.InvariantCulture)}.",
            NativeTheme.Muted);
        current.AutomationId = $"lifestyle-increments-current-{token}";
        body.Add(current);

        if (!state.CareerMode)
        {
            _decrease = null;
            body.Add(NativeTheme.FieldLabel($"{unit} intervals (1–100)"));
            _creationValue = new Entry
            {
                Text = state.Increments.ToString(CultureInfo.InvariantCulture),
                Keyboard = Keyboard.Numeric,
                AutomationId = $"lifestyle-increments-value-{token}",
                BackgroundColor = NativeTheme.Surface,
                TextColor = NativeTheme.Text,
                MaxLength = 3
            };
            body.Add(_creationValue);
            _primary = ActionButton(
                "Set intervals",
                $"lifestyle-increments-set-{token}",
                CharacterLifestyleIncrementAction.SetCreation);
            body.Add(_primary);
        }
        else
        {
            _creationValue = null;
            body.Add(NativeTheme.Body(
                state.TotalIncrementCostExact
                    ? $"One additional {unit.ToLowerInvariant()} interval costs {state.TotalIncrementCost.ToString(CultureInfo.InvariantCulture)} Nuyen. Available: {(state.NuyenExact ? state.Nuyen.ToString(CultureInfo.InvariantCulture) : "unverified")}."
                    : "The saved total interval cost is not exact; purchases are unavailable.",
                state.TotalIncrementCostExact ? NativeTheme.Muted : NativeTheme.Danger));
            _decrease = ActionButton(
                $"Decrease by one {unit.ToLowerInvariant()} interval",
                $"lifestyle-increments-decrease-{token}",
                CharacterLifestyleIncrementAction.DecreaseCareer);
            _primary = ActionButton(
                $"Purchase one {unit.ToLowerInvariant()} interval",
                $"lifestyle-increments-increase-{token}",
                CharacterLifestyleIncrementAction.IncreaseCareer);
            body.Add(_decrease);
            body.Add(_primary);
        }

        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private Button ActionButton(
        string label,
        string automationId,
        CharacterLifestyleIncrementAction action)
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
        if (_creationValue is not null)
        {
            _creationValue.IsEnabled = revisionMatches;
        }
        _primary.IsEnabled = revisionMatches && CharacterLifestyleIncrementRules.Quote(
            _state,
            _state.CareerMode
                ? CharacterLifestyleIncrementAction.IncreaseCareer
                : CharacterLifestyleIncrementAction.SetCreation,
            _state.CareerMode
                ? null
                : Math.Clamp(
                    _state.Increments,
                    CharacterLifestyleIncrementRules.CreationMinimum,
                    CharacterLifestyleIncrementRules.CreationMaximum)).Exact;
        if (_decrease is not null)
        {
            _decrease.IsEnabled = revisionMatches && CharacterLifestyleIncrementRules.Quote(
                _state,
                CharacterLifestyleIncrementAction.DecreaseCareer).Exact;
        }
    }

    private async Task ApplyAsync(CharacterLifestyleIncrementAction action)
    {
        int? requested = null;
        if (action == CharacterLifestyleIncrementAction.SetCreation)
        {
            if (!int.TryParse(_creationValue?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                await DisplayAlertAsync("Invalid intervals", "Enter a whole number from 1 to 100.", "OK");
                return;
            }
            requested = value;
        }

        CharacterLifestyleIncrementQuote quote = CharacterLifestyleIncrementRules.Quote(_state, action, requested);
        if (!quote.Exact)
        {
            await DisplayAlertAsync("Lifestyle intervals unavailable", quote.Blocker ?? "This edit is not exact.", "OK");
            return;
        }
        if (quote.UpdatedIncrements == _state.Increments)
        {
            await Navigation.PopAsync();
            return;
        }

        await Coordinator.ApplyLifestyleIncrementEditAsync(new LifestyleIncrementEditRequest(
            _workspaceId,
            _contentRevision,
            _state.LifestyleId,
            action,
            requested,
            _state));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }

    private static string UnitLabel(CharacterLifestyleIncrementUnit unit)
        => unit switch
        {
            CharacterLifestyleIncrementUnit.Day => "Day",
            CharacterLifestyleIncrementUnit.Week => "Week",
            _ => "Month"
        };
}
