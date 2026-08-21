using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class WeaponAccessoryIncludedPage : NativePageBase
{
    private readonly CharacterWorkspaceId _workspaceId;
    private readonly long _contentRevision;
    private readonly Guid _weaponId;
    private readonly Guid _accessoryId;
    private readonly bool _initialValue;
    private readonly Switch _includedInWeapon;
    private readonly Button _save;

    public WeaponAccessoryIncludedPage(
        RunnerSessionCoordinator coordinator,
        CharacterWorkspaceId workspaceId,
        long contentRevision,
        Guid weaponId,
        Guid accessoryId,
        string accessoryName,
        bool includedInWeapon) : base(coordinator)
    {
        if (weaponId == Guid.Empty || accessoryId == Guid.Empty)
        {
            throw new ArgumentException(
                "Included-in-weapon editing requires stable weapon and accessory identities.",
                nameof(accessoryId));
        }

        _workspaceId = workspaceId;
        _contentRevision = contentRevision;
        _weaponId = weaponId;
        _accessoryId = accessoryId;
        _initialValue = includedInWeapon;
        string targetToken = accessoryId.ToString("N");
        Title = "Included in Weapon";
        AutomationId = $"weapon-accessory-included-page-{targetToken}";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Selected weapon accessory"));
        body.Add(NativeTheme.Title(string.IsNullOrEmpty(accessoryName) ? "Weapon Accessory" : accessoryName));
        body.Add(NativeTheme.Body(
            "Included accessories are part of the base weapon configuration and contribute no separate cost or weight.",
            NativeTheme.Muted));

        _includedInWeapon = new Switch
        {
            IsToggled = includedInWeapon,
            AutomationId = $"weapon-accessory-included-toggle-{targetToken}",
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
        row.Add(NativeTheme.FieldLabel("Included in Weapon"), 0, 0);
        row.Add(_includedInWeapon, 1, 0);
        body.Add(NativeTheme.Card(row));

        _save = NativeTheme.PrimaryButton("Save Included State");
        _save.AutomationId = $"weapon-accessory-included-save-{targetToken}";
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
        _includedInWeapon.IsEnabled = revisionMatches;
        _save.IsEnabled = revisionMatches;
    }

    private async Task SaveAsync()
    {
        if (_includedInWeapon.IsToggled == _initialValue)
        {
            await Navigation.PopAsync();
            return;
        }

        await Coordinator.ApplyWeaponAccessoryIncludedEditAsync(new WeaponAccessoryIncludedEditRequest(
            _workspaceId,
            _contentRevision,
            _weaponId,
            _accessoryId,
            _includedInWeapon.IsToggled));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
