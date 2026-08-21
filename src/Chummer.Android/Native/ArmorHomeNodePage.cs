using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class ArmorHomeNodePage : NativePageBase
{
    private readonly CharacterWorkspaceId _workspaceId;
    private readonly long _contentRevision;
    private readonly Guid _armorId;
    private readonly bool _initialValue;
    private readonly Switch _homeNode;
    private readonly Button _save;

    public ArmorHomeNodePage(
        RunnerSessionCoordinator coordinator,
        CharacterWorkspaceId workspaceId,
        long contentRevision,
        Guid armorId,
        string armorName,
        bool homeNode) : base(coordinator)
    {
        if (armorId == Guid.Empty)
        {
            throw new ArgumentException("Armor home-node editing requires a stable armor identity.", nameof(armorId));
        }

        _workspaceId = workspaceId;
        _contentRevision = contentRevision;
        _armorId = armorId;
        _initialValue = homeNode;
        string targetToken = armorId.ToString("N");
        Title = "Armor Home Node";
        AutomationId = $"armor-home-node-page-{targetToken}";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Selected armor"));
        body.Add(NativeTheme.Title(string.IsNullOrEmpty(armorName) ? "Armor Home Node" : armorName));
        body.Add(NativeTheme.Body(
            "A runner can have one Home Node. Enabling this armor clears the saved Home Node flag from every other device.",
            NativeTheme.Muted));

        _homeNode = new Switch
        {
            IsToggled = homeNode,
            AutomationId = $"armor-home-node-toggle-{targetToken}",
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
        _save.AutomationId = $"armor-home-node-save-{targetToken}";
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

        await Coordinator.ApplyArmorHomeNodeEditAsync(new ArmorHomeNodeEditRequest(
            _workspaceId,
            _contentRevision,
            _armorId,
            _homeNode.IsToggled));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
