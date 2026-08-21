using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class ArmorEquipmentPage : NativePageBase
{
    private readonly CharacterWorkspaceId _workspaceId;
    private readonly long _contentRevision;
    private readonly CharacterArmorEquipmentState _state;
    private readonly Switch _equipped;
    private readonly Button _save;
    private readonly Button _equipAll;
    private readonly Button _unequipAll;

    public ArmorEquipmentPage(
        RunnerSessionCoordinator coordinator,
        CharacterWorkspaceId workspaceId,
        long contentRevision,
        string armorName,
        CharacterArmorEquipmentState state) : base(coordinator)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.ArmorId == Guid.Empty)
        {
            throw new ArgumentException("Armor equipment editing requires a stable armor identity.", nameof(state));
        }

        _workspaceId = workspaceId;
        _contentRevision = contentRevision;
        _state = state;
        string token = state.ArmorId.ToString("N");
        Title = "Armor Equipment";
        AutomationId = $"armor-equipment-page-{token}";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Selected armor"));
        body.Add(NativeTheme.Title(string.IsNullOrWhiteSpace(armorName) ? "Armor" : armorName));
        body.Add(NativeTheme.Body(
            $"{state.EquippedCount} of {state.ArmorCount} armor items are equipped. "
            + "Bulk actions preserve each armor mod and child gear's saved equipped flag.",
            NativeTheme.Muted));

        _equipped = new Switch
        {
            IsToggled = state.Equipped,
            AutomationId = $"armor-equipment-toggle-{token}",
            OnColor = NativeTheme.Signal
        };
        Grid row = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        row.Add(NativeTheme.FieldLabel("Equipped"), 0, 0);
        row.Add(_equipped, 1, 0);
        body.Add(NativeTheme.Card(row));

        _save = NativeTheme.PrimaryButton("Save Equipped State");
        _save.AutomationId = $"armor-equipment-save-{token}";
        _save.Clicked += async (_, _) => await RunAsync(SaveSelectedAsync);
        body.Add(_save);

        _equipAll = NativeTheme.SecondaryButton("Equip All Armor");
        _equipAll.AutomationId = $"armor-equipment-equip-all-{token}";
        _equipAll.Clicked += async (_, _) => await RunAsync(
            () => ApplyAsync(CharacterArmorEquipmentAction.EquipAll));
        body.Add(_equipAll);

        _unequipAll = NativeTheme.SecondaryButton("Unequip All Armor");
        _unequipAll.AutomationId = $"armor-equipment-unequip-all-{token}";
        _unequipAll.Clicked += async (_, _) => await RunAsync(
            () => ApplyAsync(CharacterArmorEquipmentAction.UnequipAll));
        body.Add(_unequipAll);

        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private void RefreshEnabledState()
    {
        bool current = Coordinator.State.WorkspaceId == _workspaceId
            && Coordinator.State.ContentRevision == _contentRevision;
        _equipped.IsEnabled = current;
        _save.IsEnabled = current;
        _equipAll.IsEnabled = current && _state.CanEquipAll;
        _unequipAll.IsEnabled = current && _state.CanUnequipAll;
    }

    private Task SaveSelectedAsync()
    {
        if (_equipped.IsToggled == _state.Equipped)
        {
            return Navigation.PopAsync();
        }
        return ApplyAsync(_equipped.IsToggled
            ? CharacterArmorEquipmentAction.EquipSelected
            : CharacterArmorEquipmentAction.UnequipSelected);
    }

    private async Task ApplyAsync(CharacterArmorEquipmentAction action)
    {
        await Coordinator.ApplyArmorEquipmentEditAsync(new ArmorEquipmentEditRequest(
            _workspaceId,
            _contentRevision,
            _state.ArmorId,
            _state.Equipped,
            _state.ArmorCount,
            _state.EquippedCount,
            action));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
