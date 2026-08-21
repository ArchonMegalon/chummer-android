using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class CyberwareActiveCommlinkPage : NativePageBase
{
    private readonly CharacterWorkspaceId _workspaceId;
    private readonly long _contentRevision;
    private readonly CharacterCyberwareActiveCommlinkSemantics _semantics;
    private readonly Switch _activeCommlink;
    private readonly Button _save;

    public CyberwareActiveCommlinkPage(
        RunnerSessionCoordinator coordinator,
        CharacterWorkspaceId workspaceId,
        long contentRevision,
        Guid cyberwareId,
        string cyberwareName,
        CharacterCyberwareActiveCommlinkSemantics semantics) : base(coordinator)
    {
        ArgumentNullException.ThrowIfNull(semantics);
        if (cyberwareId == Guid.Empty
            || semantics.CyberwareId != cyberwareId
            || !semantics.IsCommlink)
        {
            throw new ArgumentException(
                "Cyberware active-commlink editing requires exact persona eligibility bound to one stable cyberware identity.",
                nameof(semantics));
        }

        _workspaceId = workspaceId;
        _contentRevision = contentRevision;
        _semantics = semantics;
        string targetToken = cyberwareId.ToString("N");
        Title = "Cyberware Active Commlink";
        AutomationId = $"cyberware-active-commlink-page-{targetToken}";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Selected cyberware"));
        body.Add(NativeTheme.Title(string.IsNullOrEmpty(cyberwareName)
            ? "Cyberware Active Commlink"
            : cyberwareName));
        body.Add(NativeTheme.Body(
            "A runner can have one active commlink. Enabling this persona-capable cyberware clears the saved active flag from every other Matrix device.",
            NativeTheme.Muted));

        _activeCommlink = new Switch
        {
            IsToggled = semantics.ActiveCommlink,
            AutomationId = $"cyberware-active-commlink-toggle-{targetToken}",
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
        _save.AutomationId = $"cyberware-active-commlink-save-{targetToken}";
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

        await Coordinator.ApplyCyberwareActiveCommlinkEditAsync(new CyberwareActiveCommlinkEditRequest(
            _workspaceId,
            _contentRevision,
            _semantics.CyberwareId,
            _activeCommlink.IsToggled,
            _semantics));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
