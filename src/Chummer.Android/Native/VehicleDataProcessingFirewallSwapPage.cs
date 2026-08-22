using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class VehicleDataProcessingFirewallSwapPage : NativePageBase
{
    private sealed record StatOption(CharacterVehicleMatrixStat Value, string Label);
    private static readonly StatOption[] ChangedOptions =
    [
        new(CharacterVehicleMatrixStat.Attack, "Attack"),
        new(CharacterVehicleMatrixStat.Sleaze, "Sleaze"),
        new(CharacterVehicleMatrixStat.DataProcessing, "Data Processing"),
        new(CharacterVehicleMatrixStat.Firewall, "Firewall")
    ];
    private static readonly StatOption[] AllTargets =
    [
        new(CharacterVehicleMatrixStat.Attack, "Attack"),
        new(CharacterVehicleMatrixStat.Sleaze, "Sleaze"),
        new(CharacterVehicleMatrixStat.DataProcessing, "Data Processing"),
        new(CharacterVehicleMatrixStat.Firewall, "Firewall")
    ];

    private readonly VehicleDataProcessingFirewallSwapEditorState _editor;
    private readonly Picker _changed;
    private readonly Picker _target;
    private readonly Button _save;
    private StatOption[] _targets = [];

    public VehicleDataProcessingFirewallSwapPage(
        RunnerSessionCoordinator coordinator,
        VehicleDataProcessingFirewallSwapEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        CharacterVehicleMatrixSwapState vehicle = editor.Vehicle;
        if (!CharacterVehicleMatrixSwapRules.IsValidIdentity(vehicle.Identity)
            || vehicle.Economics is not { NuyenDelta: 0m, KarmaDelta: 0 }
            || !vehicle.Provenance.CanSwapAttributes)
            throw new ArgumentException("Vehicle Matrix swapping requires an exact eligible root Vehicle.", nameof(editor));

        string token = vehicle.Identity.VehicleId.ToString("N");
        Title = "Vehicle Matrix values";
        AutomationId = $"vehicle-dp-firewall-swap-page-{token}";
        var body = new VerticalStackLayout { Padding = new Thickness(20, 18, 20, 40), Spacing = 14 };
        body.Add(NativeTheme.Eyebrow("Create + Career Vehicle Matrix"));
        body.Add(NativeTheme.Title(vehicle.DisplayName));
        body.Add(NativeTheme.Body(
            "Swap one saved raw Attack, Sleaze, Data Processing, or Firewall value. Bonuses, sensor, active/home state, parents, and costs remain unchanged.",
            NativeTheme.Muted));
        Label values = NativeTheme.Body(
            $"Saved raw values · Attack {vehicle.Attack} · Sleaze {vehicle.Sleaze} · Data Processing {vehicle.DataProcessing} · Firewall {vehicle.Firewall}",
            NativeTheme.Muted);
        values.AutomationId = $"vehicle-dp-firewall-swap-values-{token}";
        body.Add(values);

        _changed = new Picker
        {
            Title = "Changed Matrix attribute", ItemsSource = ChangedOptions,
            ItemDisplayBinding = new Binding(nameof(StatOption.Label)), SelectedIndex = 0,
            AutomationId = $"vehicle-dp-firewall-swap-changed-{token}",
            BackgroundColor = NativeTheme.Surface, TextColor = NativeTheme.Text
        };
        _changed.SelectedIndexChanged += (_, _) => RefreshTargets();
        body.Add(NativeTheme.FieldLabel("Changed saved attribute")); body.Add(_changed);
        _target = new Picker
        {
            Title = "Swap with", ItemDisplayBinding = new Binding(nameof(StatOption.Label)),
            AutomationId = $"vehicle-dp-firewall-swap-target-{token}",
            BackgroundColor = NativeTheme.Surface, TextColor = NativeTheme.Text
        };
        body.Add(NativeTheme.FieldLabel("Other saved attribute")); body.Add(_target);
        _save = NativeTheme.PrimaryButton("Swap saved raw values");
        _save.AutomationId = $"vehicle-dp-firewall-swap-save-{token}";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);
        Content = new ScrollView { Content = body };
        RefreshTargets();
    }

    protected override void Refresh() => RefreshEnabled();

    private void RefreshTargets()
    {
        CharacterVehicleMatrixStat changed = ChangedOptions[
            _changed.SelectedIndex >= 0 && _changed.SelectedIndex < ChangedOptions.Length
                ? _changed.SelectedIndex : 0].Value;
        _targets = AllTargets.Where(option => option.Value != changed).ToArray();
        _target.ItemsSource = _targets;
        _target.SelectedIndex = 0;
        RefreshEnabled();
    }

    private void RefreshEnabled()
    {
        bool current = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        _changed.IsEnabled = current;
        _target.IsEnabled = current && _targets.Length == AllTargets.Length - 1;
        _save.IsEnabled = _target.IsEnabled;
    }

    private async Task SaveAsync()
    {
        if (_changed.SelectedIndex < 0 || _changed.SelectedIndex >= ChangedOptions.Length
            || _target.SelectedIndex < 0 || _target.SelectedIndex >= _targets.Length) return;
        CharacterVehicleMatrixStat changed = ChangedOptions[_changed.SelectedIndex].Value;
        CharacterVehicleMatrixStat target = _targets[_target.SelectedIndex].Value;
        if (!CharacterVehicleMatrixSwapRules.TryValidateMutation(
                _editor.Vehicle, _editor.Vehicle.Revision, changed, target))
        {
            await DisplayAlertAsync("Different values required",
                "Choose two different saved raw Matrix values on the eligible Vehicle root.", "OK");
            return;
        }
        await Coordinator.ApplyVehicleDataProcessingFirewallSwapEditAsync(new(
            _editor.WorkspaceId, _editor.ContentRevision, _editor.Vehicle.Identity,
            _editor.Vehicle.Revision, changed, target));
        if (Coordinator.State.Error is null) await Navigation.PopAsync();
    }
}
