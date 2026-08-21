using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class GearActiveCommlinkPage : NativePageBase
{
    private readonly CharacterWorkspaceId _workspaceId;
    private readonly long _contentRevision;
    private readonly CharacterGearActiveCommlinkSemantics _semantics;
    private readonly Switch _activeCommlink;
    private readonly Button _save;

    public GearActiveCommlinkPage(
        RunnerSessionCoordinator coordinator,
        CharacterWorkspaceId workspaceId,
        long contentRevision,
        Guid gearId,
        string gearName,
        CharacterGearActiveCommlinkSemantics semantics) : base(coordinator)
    {
        ArgumentNullException.ThrowIfNull(semantics);
        if (gearId == Guid.Empty
            || semantics.GearId != gearId
            || !semantics.IsCommlink)
        {
            throw new ArgumentException(
                "Gear active-commlink editing requires exact persona eligibility bound to one stable gear identity.",
                nameof(semantics));
        }

        _workspaceId = workspaceId;
        _contentRevision = contentRevision;
        _semantics = semantics;
        string targetToken = gearId.ToString("N");
        Title = "Gear Active Commlink";
        AutomationId = $"gear-active-commlink-page-{targetToken}";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Selected gear"));
        body.Add(NativeTheme.Title(string.IsNullOrEmpty(gearName) ? "Gear Active Commlink" : gearName));
        body.Add(NativeTheme.Body(
            "A runner can have one active commlink. Enabling this persona-capable gear clears the saved active flag from every other Matrix device.",
            NativeTheme.Muted));

        _activeCommlink = new Switch
        {
            IsToggled = semantics.ActiveCommlink,
            AutomationId = $"gear-active-commlink-toggle-{targetToken}",
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
        _save.AutomationId = $"gear-active-commlink-save-{targetToken}";
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

        await Coordinator.ApplyGearActiveCommlinkEditAsync(new GearActiveCommlinkEditRequest(
            _workspaceId,
            _contentRevision,
            _semantics.GearId,
            _activeCommlink.IsToggled,
            _semantics));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
