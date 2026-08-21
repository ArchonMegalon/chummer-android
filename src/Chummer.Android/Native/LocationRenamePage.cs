using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class LocationRenamePage : NativePageBase
{
    private readonly CharacterWorkspaceId _workspaceId;
    private readonly long _contentRevision;
    private readonly WorkspaceLocationKind _kind;
    private readonly WorkspaceLocationItemState _location;
    private readonly Entry _name;
    private readonly Button _save;

    public LocationRenamePage(
        RunnerSessionCoordinator coordinator,
        CharacterWorkspaceId workspaceId,
        long contentRevision,
        WorkspaceLocationKind kind,
        WorkspaceLocationItemState location) : base(coordinator)
    {
        _workspaceId = workspaceId;
        _contentRevision = contentRevision;
        _kind = kind;
        _location = location ?? throw new ArgumentNullException(nameof(location));
        Title = "Rename location";
        AutomationId = "location-rename-page";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow($"{KindLabel(kind)} organization"));
        body.Add(NativeTheme.Title("Rename location"));
        body.Add(NativeTheme.Body(
            "Change this location's display name. Chummer5 preserves the exact nonempty text.",
            NativeTheme.Muted));
        body.Add(NativeTheme.FieldLabel("Location name"));

        _name = new Entry
        {
            AutomationId = "location-rename-name",
            Text = location.Name,
            MaxLength = LocationRenameRequest.MaximumNameLength,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _name.TextChanged += (_, _) => RefreshEnabledState();
        body.Add(_name);

        _save = NativeTheme.PrimaryButton("Rename location");
        _save.AutomationId = "location-rename-save";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);
        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private void RefreshEnabledState()
    {
        _save.IsEnabled = !string.IsNullOrEmpty(_name.Text)
            && Coordinator.State.WorkspaceId == _workspaceId
            && Coordinator.State.ContentRevision == _contentRevision;
    }

    private async Task SaveAsync()
    {
        string name = _name.Text ?? string.Empty;
        if (string.IsNullOrEmpty(name))
        {
            await DisplayAlertAsync("Location name required", "Enter a location name.", "OK");
            return;
        }

        await Coordinator.ApplyLocationRenameAsync(new LocationRenameRequest(
            _workspaceId,
            _contentRevision,
            _kind,
            _location.Id,
            name));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }

    private static string KindLabel(WorkspaceLocationKind kind)
        => kind switch
        {
            WorkspaceLocationKind.Gear => "Gear",
            WorkspaceLocationKind.Weapon => "Weapon",
            WorkspaceLocationKind.Armor => "Armor",
            WorkspaceLocationKind.Vehicle => "Vehicle",
            _ => "Runner"
        };
}
