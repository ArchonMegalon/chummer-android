using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class WeaponActiveCommlinkPage : NativePageBase
{
    private readonly CharacterWorkspaceId _workspaceId;
    private readonly long _contentRevision;
    private readonly CharacterWeaponActiveCommlinkSemantics _semantics;
    private readonly Switch _activeCommlink;
    private readonly Button _save;

    public WeaponActiveCommlinkPage(
        RunnerSessionCoordinator coordinator,
        CharacterWorkspaceId workspaceId,
        long contentRevision,
        Guid weaponId,
        string weaponName,
        CharacterWeaponActiveCommlinkSemantics semantics) : base(coordinator)
    {
        ArgumentNullException.ThrowIfNull(semantics);
        if (weaponId == Guid.Empty
            || semantics.WeaponId != weaponId
            || !semantics.IsCommlink)
        {
            throw new ArgumentException(
                "Weapon active-commlink editing requires an exact commlink rule bound to one stable weapon.",
                nameof(semantics));
        }

        _workspaceId = workspaceId;
        _contentRevision = contentRevision;
        _semantics = semantics;
        string targetToken = weaponId.ToString("N");
        Title = "Weapon Active Commlink";
        AutomationId = $"weapon-active-commlink-page-{targetToken}";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Selected weapon"));
        body.Add(NativeTheme.Title(string.IsNullOrEmpty(weaponName) ? "Weapon Active Commlink" : weaponName));
        body.Add(NativeTheme.Body(
            "A runner can have one active commlink. Enabling this weapon clears the saved active flag from every other Matrix device.",
            NativeTheme.Muted));
        body.Add(NativeTheme.Card(new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                NativeTheme.Metric("Matrix owner", semantics.MatrixOwnerKind)
            }
        }));

        _activeCommlink = new Switch
        {
            IsToggled = semantics.ActiveCommlink,
            AutomationId = $"weapon-active-commlink-toggle-{targetToken}",
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
        _save.AutomationId = $"weapon-active-commlink-save-{targetToken}";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);
        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private void RefreshEnabledState()
    {
        bool canEdit = Coordinator.State.WorkspaceId == _workspaceId
            && Coordinator.State.ContentRevision == _contentRevision;
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

        await Coordinator.ApplyWeaponActiveCommlinkEditAsync(new WeaponActiveCommlinkEditRequest(
            _workspaceId,
            _contentRevision,
            _semantics.WeaponId,
            _activeCommlink.IsToggled,
            _semantics));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
