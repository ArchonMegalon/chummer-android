using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class ArmorDamagePage : NativePageBase
{
    private readonly CharacterWorkspaceId _workspaceId;
    private readonly long _contentRevision;
    private readonly WorkspaceArmorDamageAdjustmentState _state;
    private readonly Button _repair;
    private readonly Button _degrade;

    public ArmorDamagePage(
        RunnerSessionCoordinator coordinator,
        CharacterWorkspaceId workspaceId,
        long contentRevision,
        string armorName,
        WorkspaceArmorDamageAdjustmentState state) : base(coordinator)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.ArmorId == Guid.Empty)
        {
            throw new ArgumentException("Armor condition editing requires a stable armor identity.", nameof(state));
        }

        _workspaceId = workspaceId;
        _contentRevision = contentRevision;
        _state = state;
        string token = state.ArmorId.ToString("N");
        Title = "Armor Condition";
        AutomationId = $"armor-damage-page-{token}";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Career armor degradation"));
        body.Add(NativeTheme.Title(string.IsNullOrWhiteSpace(armorName) ? "Armor" : armorName));
        body.Add(NativeTheme.Body(
            $"Damage {state.Damage.ToString()}/{state.Maximum.ToString()}. Repair increases the remaining Armor value; degrade decreases it.",
            NativeTheme.Muted));

        _repair = NativeTheme.SecondaryButton("Repair Armor (+A)");
        _repair.AutomationId = $"armor-damage-repair-{token}";
        _repair.Clicked += async (_, _) => await RunAsync(
            () => AdjustAsync(CharacterArmorDamageAdjustment.Repair));
        body.Add(_repair);

        _degrade = NativeTheme.SecondaryButton("Degrade Armor (-A)");
        _degrade.AutomationId = $"armor-damage-degrade-{token}";
        _degrade.Clicked += async (_, _) => await RunAsync(
            () => AdjustAsync(CharacterArmorDamageAdjustment.Degrade));
        body.Add(_degrade);

        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private void RefreshEnabledState()
    {
        bool revisionMatches = Coordinator.State.WorkspaceId == _workspaceId
            && Coordinator.State.ContentRevision == _contentRevision;
        _repair.IsEnabled = revisionMatches && _state.CanRepair;
        _degrade.IsEnabled = revisionMatches && _state.CanDegrade;
    }

    private async Task AdjustAsync(CharacterArmorDamageAdjustment adjustment)
    {
        await Coordinator.ApplyArmorDamageAdjustmentAsync(new ArmorDamageAdjustmentRequest(
            _workspaceId,
            _contentRevision,
            _state.ArmorId,
            _state.Damage,
            _state.Maximum,
            adjustment));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
