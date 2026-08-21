using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class WeaponLocationAddPage : NativePageBase
{
    private readonly CharacterWorkspaceId _workspaceId;
    private readonly long _contentRevision;
    private readonly Entry _name;
    private readonly Button _add;

    public WeaponLocationAddPage(
        RunnerSessionCoordinator coordinator,
        CharacterWorkspaceId workspaceId,
        long contentRevision) : base(coordinator)
    {
        _workspaceId = workspaceId;
        _contentRevision = contentRevision;
        Title = "Add weapon location";
        AutomationId = "weapon-location-add-page";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Weapon organization"));
        body.Add(NativeTheme.Title("Add weapon location"));
        body.Add(NativeTheme.Body(
            "Name a new location for this runner's weapons. The exact text is preserved.",
            NativeTheme.Muted));
        body.Add(NativeTheme.FieldLabel("Location name"));

        _name = new Entry
        {
            AutomationId = "weapon-location-name",
            Placeholder = "Armory rack",
            MaxLength = WeaponLocationAddRequest.MaximumNameLength,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _name.TextChanged += (_, _) => RefreshEnabledState();
        body.Add(_name);

        _add = NativeTheme.PrimaryButton("Add location");
        _add.AutomationId = "weapon-location-add";
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
            await DisplayAlertAsync("Location name required", "Enter a weapon location name.", "OK");
            return;
        }

        await Coordinator.ApplyWeaponLocationAddAsync(new WeaponLocationAddRequest(
            _workspaceId,
            _contentRevision,
            name));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
