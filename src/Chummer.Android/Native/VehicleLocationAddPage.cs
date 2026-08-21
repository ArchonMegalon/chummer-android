using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class VehicleLocationAddPage : NativePageBase
{
    private readonly CharacterWorkspaceId _workspaceId;
    private readonly long _contentRevision;
    private readonly Guid? _vehicleId;
    private readonly Entry _name;
    private readonly Button _add;

    public VehicleLocationAddPage(
        RunnerSessionCoordinator coordinator,
        CharacterWorkspaceId workspaceId,
        long contentRevision,
        Guid? vehicleId,
        string? vehicleName) : base(coordinator)
    {
        _workspaceId = workspaceId;
        _contentRevision = contentRevision;
        _vehicleId = vehicleId;
        string targetToken = vehicleId?.ToString("N") ?? "global";
        string targetLabel = vehicleId is null
            ? "this runner's vehicles"
            : string.IsNullOrEmpty(vehicleName) ? "the selected vehicle" : vehicleName;
        Title = "Add vehicle location";
        AutomationId = $"vehicle-location-add-page-{targetToken}";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow(vehicleId is null ? "Vehicle organization" : "Selected vehicle"));
        body.Add(NativeTheme.Title("Add vehicle location"));
        body.Add(NativeTheme.Body(
            $"Name a new location for {targetLabel}. The exact text is preserved.",
            NativeTheme.Muted));
        body.Add(NativeTheme.FieldLabel("Location name"));

        _name = new Entry
        {
            AutomationId = $"vehicle-location-name-{targetToken}",
            Placeholder = vehicleId is null ? "Team garage" : "Smuggling compartment",
            MaxLength = VehicleLocationAddRequest.MaximumNameLength,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _name.TextChanged += (_, _) => RefreshEnabledState();
        body.Add(_name);

        _add = NativeTheme.PrimaryButton("Add location");
        _add.AutomationId = $"vehicle-location-add-{targetToken}";
        _add.Clicked += async (_, _) => await RunAsync(AddAsync);
        body.Add(_add);
        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private void RefreshEnabledState()
    {
        _add.IsEnabled = !string.IsNullOrEmpty(_name.Text)
            && Coordinator.State.WorkspaceId == _workspaceId
            && Coordinator.State.ContentRevision == _contentRevision;
    }

    private async Task AddAsync()
    {
        string name = _name.Text ?? string.Empty;
        if (string.IsNullOrEmpty(name))
        {
            await DisplayAlertAsync("Location name required", "Enter a vehicle location name.", "OK");
            return;
        }

        await Coordinator.ApplyVehicleLocationAddAsync(new VehicleLocationAddRequest(
            _workspaceId,
            _contentRevision,
            _vehicleId,
            name));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
