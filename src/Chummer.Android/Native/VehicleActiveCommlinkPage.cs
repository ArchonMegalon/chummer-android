using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class VehicleActiveCommlinkPage : NativePageBase
{
    private readonly CharacterWorkspaceId _workspaceId;
    private readonly long _contentRevision;
    private readonly CharacterVehicleActiveCommlinkSemantics _semantics;
    private readonly Switch _activeCommlink;
    private readonly Button _save;

    public VehicleActiveCommlinkPage(
        RunnerSessionCoordinator coordinator,
        CharacterWorkspaceId workspaceId,
        long contentRevision,
        Guid vehicleId,
        string vehicleName,
        CharacterVehicleActiveCommlinkSemantics semantics) : base(coordinator)
    {
        ArgumentNullException.ThrowIfNull(semantics);
        if (vehicleId == Guid.Empty
            || semantics.VehicleId != vehicleId
            || !semantics.IsCommlink
            || !semantics.Visible
            || !semantics.Enabled
            || semantics.Economics is not { NuyenDelta: 0m, KarmaDelta: 0 })
        {
            throw new ArgumentException(
                "Vehicle active-commlink editing requires exact enabled persona eligibility and zero economics bound to one stable top-level Vehicle identity.",
                nameof(semantics));
        }

        _workspaceId = workspaceId;
        _contentRevision = contentRevision;
        _semantics = semantics;
        string targetToken = vehicleId.ToString("N");
        Title = "Vehicle Active Commlink";
        AutomationId = $"vehicle-active-commlink-page-{targetToken}";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Selected Vehicle"));
        body.Add(NativeTheme.Title(string.IsNullOrEmpty(vehicleName)
            ? "Vehicle Active Commlink"
            : vehicleName));
        body.Add(NativeTheme.Body(
            "A runner can have one active commlink. Enabling this persona-capable Vehicle clears the saved active flag from every other Matrix device.",
            NativeTheme.Muted));

        _activeCommlink = new Switch
        {
            IsToggled = semantics.ActiveCommlink,
            AutomationId = $"vehicle-active-commlink-toggle-{targetToken}",
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
        row.Add(NativeTheme.FieldLabel("Active Commlink"), 0, 0);
        row.Add(_activeCommlink, 1, 0);
        body.Add(NativeTheme.Card(row));

        _save = NativeTheme.PrimaryButton("Save Active Commlink");
        _save.AutomationId = $"vehicle-active-commlink-save-{targetToken}";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);
        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private void RefreshEnabledState()
    {
        bool canEdit = Coordinator.State.WorkspaceId == _workspaceId
            && Coordinator.State.ContentRevision == _contentRevision
            && _semantics.Enabled;
        _activeCommlink.IsEnabled = canEdit;
        _save.IsEnabled = canEdit;
    }

    private async Task SaveAsync()
    {
        if (_activeCommlink.IsToggled == _semantics.ActiveCommlink)
        {
            await Navigation.PopAsync();
            return;
        }

        await Coordinator.ApplyVehicleActiveCommlinkEditAsync(new VehicleActiveCommlinkEditRequest(
            _workspaceId,
            _contentRevision,
            _semantics.VehicleId,
            _activeCommlink.IsToggled,
            _semantics));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
