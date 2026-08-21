using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class VehicleHomeNodePage : NativePageBase
{
    private readonly CharacterWorkspaceId _workspaceId;
    private readonly long _contentRevision;
    private readonly Guid _vehicleId;
    private readonly bool _initialValue;
    private readonly Switch _homeNode;
    private readonly Button _save;

    public VehicleHomeNodePage(
        RunnerSessionCoordinator coordinator,
        CharacterWorkspaceId workspaceId,
        long contentRevision,
        Guid vehicleId,
        string vehicleName,
        bool homeNode) : base(coordinator)
    {
        if (vehicleId == Guid.Empty)
        {
            throw new ArgumentException("Vehicle home-node editing requires a stable vehicle identity.", nameof(vehicleId));
        }

        _workspaceId = workspaceId;
        _contentRevision = contentRevision;
        _vehicleId = vehicleId;
        _initialValue = homeNode;
        string targetToken = vehicleId.ToString("N");
        Title = "Vehicle Home Node";
        AutomationId = $"vehicle-home-node-page-{targetToken}";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Selected vehicle"));
        body.Add(NativeTheme.Title(string.IsNullOrEmpty(vehicleName) ? "Vehicle Home Node" : vehicleName));
        body.Add(NativeTheme.Body(
            "A runner can have one Home Node. Enabling this vehicle clears the saved Home Node flag from every other device.",
            NativeTheme.Muted));

        _homeNode = new Switch
        {
            IsToggled = homeNode,
            AutomationId = $"vehicle-home-node-toggle-{targetToken}",
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
        row.Add(NativeTheme.FieldLabel("Home Node"), 0, 0);
        row.Add(_homeNode, 1, 0);
        body.Add(NativeTheme.Card(row));

        _save = NativeTheme.PrimaryButton("Save Home Node");
        _save.AutomationId = $"vehicle-home-node-save-{targetToken}";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);
        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private void RefreshEnabledState()
    {
        bool revisionMatches = Coordinator.State.WorkspaceId == _workspaceId
            && Coordinator.State.ContentRevision == _contentRevision;
        _homeNode.IsEnabled = revisionMatches;
        _save.IsEnabled = revisionMatches;
    }

    private async Task SaveAsync()
    {
        if (_homeNode.IsToggled == _initialValue)
        {
            await Navigation.PopAsync();
            return;
        }

        await Coordinator.ApplyVehicleHomeNodeEditAsync(new VehicleHomeNodeEditRequest(
            _workspaceId,
            _contentRevision,
            _vehicleId,
            _homeNode.IsToggled));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
