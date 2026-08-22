using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class VehicleEquipmentInstalledPage : NativePageBase
{
    private sealed record NodeOption(CharacterVehicleEquipmentInstalledState State, string Label);

    private readonly VehicleEquipmentInstalledEditorState _editor;
    private readonly IReadOnlyList<NodeOption> _options;
    private readonly Picker _target;
    private readonly Switch _installed;
    private readonly Label _eligibility;
    private readonly Button _save;

    public VehicleEquipmentInstalledPage(
        RunnerSessionCoordinator coordinator,
        VehicleEquipmentInstalledEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        if (editor.VehicleId == Guid.Empty
            || editor.Nodes.Count == 0
            || editor.Nodes.Any(node =>
                !CharacterVehicleEquipmentInstalledRules.IsValidIdentity(node.Identity)
                || node.Identity.VehicleId != editor.VehicleId
                || node.Economics is not { NuyenDelta: 0m, KarmaDelta: 0 }))
        {
            throw new ArgumentException(
                "Vehicle Installed editing requires exact zero-economic typed nodes under one stable Vehicle.",
                nameof(editor));
        }

        string vehicleToken = editor.VehicleId.ToString("N");
        Title = "Vehicle Installed";
        AutomationId = $"vehicle-equipment-installed-page-{vehicleToken}";
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Selected Vehicle tree"));
        body.Add(NativeTheme.Title("Installed"));
        body.Add(NativeTheme.Body(
            "Choose an exact Weapon Mount, Vehicle Mod, Weapon, or Weapon Accessory. This Create/Career state change has no Nuyen or Karma cost.",
            NativeTheme.Muted));

        _options = editor.Nodes.Select(node => new NodeOption(
            node,
            $"{node.DisplayPath} · {KindLabel(node.Identity.Path[^1].Kind)} · {node.Identity.Path[^1].Id.ToString("N")[..8]}"))
            .ToArray();
        _target = new Picker
        {
            Title = "Vehicle equipment node",
            ItemsSource = (System.Collections.IList)_options,
            ItemDisplayBinding = new Binding(nameof(NodeOption.Label)),
            SelectedIndex = 0,
            AutomationId = $"vehicle-equipment-installed-target-{vehicleToken}",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _target.SelectedIndexChanged += (_, _) => LoadSelectedState();
        body.Add(NativeTheme.FieldLabel("Selected Vehicle equipment"));
        body.Add(_target);

        _installed = new Switch
        {
            AutomationId = $"vehicle-equipment-installed-toggle-{vehicleToken}",
            OnColor = NativeTheme.Signal
        };
        SemanticProperties.SetDescription(_installed, "Installed");
        Grid row = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        row.Add(NativeTheme.FieldLabel("Installed"), 0, 0);
        row.Add(_installed, 1, 0);
        body.Add(NativeTheme.Card(row));

        _eligibility = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        _eligibility.AutomationId = $"vehicle-equipment-installed-eligibility-{vehicleToken}";
        body.Add(_eligibility);

        _save = NativeTheme.PrimaryButton("Save Installed state");
        _save.AutomationId = $"vehicle-equipment-installed-save-{vehicleToken}";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);
        Content = new ScrollView { Content = body };
        LoadSelectedState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private CharacterVehicleEquipmentInstalledState? SelectedState
        => _target.SelectedIndex >= 0 && _target.SelectedIndex < _options.Count
            ? _options[_target.SelectedIndex].State
            : null;

    private void LoadSelectedState()
    {
        if (SelectedState is { } selected)
        {
            _installed.IsToggled = selected.Installed;
            _eligibility.Text = selected.CanChangeInstalled
                ? "Exact legacy eligibility and equipped-only persistence are proven."
                : selected.LegacyEnabled
                    ? "This Vehicle Mod can recalculate saved sensor state in Chummer5, so this phone edit is fail-closed."
                    : "Chummer5 fixes this included or parent-installed node's Installed state.";
        }
        RefreshEnabledState();
    }

    private void RefreshEnabledState()
    {
        bool current = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        _target.IsEnabled = current;
        _installed.IsEnabled = current && SelectedState?.CanChangeInstalled == true;
        _save.IsEnabled = current && SelectedState?.CanChangeInstalled == true;
    }

    private async Task SaveAsync()
    {
        if (SelectedState is not { } selected)
        {
            await DisplayAlertAsync(
                "Vehicle equipment required",
                "Choose one exact saved Vehicle equipment node before saving.",
                "OK");
            return;
        }
        if (!selected.CanChangeInstalled)
        {
            await DisplayAlertAsync(
                "Installed state is fixed",
                "This node is either disabled by Chummer5 or has a side effect that this saved-data edit cannot reproduce exactly.",
                "OK");
            return;
        }
        if (_installed.IsToggled == selected.Installed)
        {
            await Navigation.PopAsync();
            return;
        }

        await Coordinator.ApplyVehicleEquipmentInstalledEditAsync(
            new VehicleEquipmentInstalledEditRequest(
                _editor.WorkspaceId,
                _editor.ContentRevision,
                selected.Identity,
                selected.Revision,
                _installed.IsToggled));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }

    private static string KindLabel(CharacterVehicleEquipmentNodeKind kind)
        => kind switch
        {
            CharacterVehicleEquipmentNodeKind.WeaponMount => "Weapon Mount",
            CharacterVehicleEquipmentNodeKind.VehicleMod => "Vehicle Mod",
            CharacterVehicleEquipmentNodeKind.Weapon => "Weapon",
            CharacterVehicleEquipmentNodeKind.WeaponAccessory => "Weapon Accessory",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
}
