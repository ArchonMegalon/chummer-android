using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class ArmorActiveCommlinkPage : NativePageBase
{
    private readonly CharacterWorkspaceId _workspaceId;
    private readonly long _contentRevision;
    private readonly Guid _armorId;
    private readonly bool _initialValue;
    private readonly Switch _activeCommlink;
    private readonly Button _save;

    public ArmorActiveCommlinkPage(
        RunnerSessionCoordinator coordinator,
        CharacterWorkspaceId workspaceId,
        long contentRevision,
        Guid armorId,
        string armorName,
        bool activeCommlink) : base(coordinator)
    {
        if (armorId == Guid.Empty)
        {
            throw new ArgumentException(
                "Armor active-commlink editing requires a stable armor identity.",
                nameof(armorId));
        }

        _workspaceId = workspaceId;
        _contentRevision = contentRevision;
        _armorId = armorId;
        _initialValue = activeCommlink;
        string targetToken = armorId.ToString("N");
        Title = "Armor Active Commlink";
        AutomationId = $"armor-active-commlink-page-{targetToken}";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Selected armor"));
        body.Add(NativeTheme.Title(string.IsNullOrEmpty(armorName) ? "Armor Active Commlink" : armorName));
        body.Add(NativeTheme.Body(
            "A runner can have one active commlink. Enabling this persona-capable armor clears the saved active flag from every other matrix device.",
            NativeTheme.Muted));

        _activeCommlink = new Switch
        {
            IsToggled = activeCommlink,
            AutomationId = $"armor-active-commlink-toggle-{targetToken}",
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
        _save.AutomationId = $"armor-active-commlink-save-{targetToken}";
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
        _activeCommlink.IsEnabled = revisionMatches;
        _save.IsEnabled = revisionMatches;
    }

    private async Task SaveAsync()
    {
        if (_activeCommlink.IsToggled == _initialValue)
        {
            await Navigation.PopAsync();
            return;
        }

        await Coordinator.ApplyArmorActiveCommlinkEditAsync(new ArmorActiveCommlinkEditRequest(
            _workspaceId,
            _contentRevision,
            _armorId,
            _activeCommlink.IsToggled));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
