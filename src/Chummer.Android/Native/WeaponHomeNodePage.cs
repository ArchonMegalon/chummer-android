using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class WeaponHomeNodePage : NativePageBase
{
    private readonly CharacterWorkspaceId _workspaceId;
    private readonly long _contentRevision;
    private readonly CharacterWeaponHomeNodeSemantics _semantics;
    private readonly Switch _homeNode;
    private readonly Button _save;

    public WeaponHomeNodePage(
        RunnerSessionCoordinator coordinator,
        CharacterWorkspaceId workspaceId,
        long contentRevision,
        Guid weaponId,
        string weaponName,
        CharacterWeaponHomeNodeSemantics semantics) : base(coordinator)
    {
        ArgumentNullException.ThrowIfNull(semantics);
        if (weaponId == Guid.Empty
            || semantics.WeaponId != weaponId
            || !semantics.Visible)
        {
            throw new ArgumentException(
                "Weapon home-node editing requires an exact visible Core rule bound to one stable weapon.",
                nameof(semantics));
        }

        _workspaceId = workspaceId;
        _contentRevision = contentRevision;
        _semantics = semantics;
        string targetToken = weaponId.ToString("N");
        Title = "Weapon Home Node";
        AutomationId = $"weapon-home-node-page-{targetToken}";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Selected weapon"));
        body.Add(NativeTheme.Title(string.IsNullOrEmpty(weaponName) ? "Weapon Home Node" : weaponName));
        body.Add(NativeTheme.Body(
            "Chummer5 permits an AI Home Node only when the weapon's Matrix owner is a commlink with enough Program Limit. Enabling it clears every other saved Home Node.",
            NativeTheme.Muted));
        body.Add(NativeTheme.Card(new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                NativeTheme.Metric("Matrix owner", semantics.MatrixOwnerKind),
                NativeTheme.Metric("Device Rating", semantics.DeviceRating.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                NativeTheme.Metric("Program Limit", semantics.ProgramLimit.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                NativeTheme.Metric("DEP", semantics.DepTotal.ToString(System.Globalization.CultureInfo.InvariantCulture))
            }
        }));
        if (!semantics.Enabled)
        {
            body.Add(NativeTheme.Body(
                "This weapon is visible to the AI, but Chummer5 currently disables Home Node editing because the Matrix-owner eligibility rule is not satisfied.",
                NativeTheme.Danger));
        }

        _homeNode = new Switch
        {
            IsToggled = semantics.HomeNode,
            AutomationId = $"weapon-home-node-toggle-{targetToken}",
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
        _save.AutomationId = $"weapon-home-node-save-{targetToken}";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);
        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private void RefreshEnabledState()
    {
        bool canEdit = _semantics.Enabled
            && Coordinator.State.WorkspaceId == _workspaceId
            && Coordinator.State.ContentRevision == _contentRevision;
        _homeNode.IsEnabled = canEdit;
        _save.IsEnabled = canEdit;
    }

    private async Task SaveAsync()
    {
        if (_homeNode.IsToggled == _semantics.HomeNode)
        {
            await Navigation.PopAsync();
            return;
        }

        await Coordinator.ApplyWeaponHomeNodeEditAsync(new WeaponHomeNodeEditRequest(
            _workspaceId,
            _contentRevision,
            _semantics.WeaponId,
            _homeNode.IsToggled,
            _semantics));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
