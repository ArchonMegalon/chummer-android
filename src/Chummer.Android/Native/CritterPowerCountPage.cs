using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class CritterPowerCountPage : NativePageBase
{
    private readonly CharacterWorkspaceId _workspaceId;
    private readonly long _contentRevision;
    private readonly CharacterCritterPowerCountState _state;
    private readonly Switch _countsTowardsLimit;
    private readonly Button _save;

    public CritterPowerCountPage(
        RunnerSessionCoordinator coordinator,
        CharacterWorkspaceId workspaceId,
        long contentRevision,
        string critterPowerName,
        CharacterCritterPowerCountState state) : base(coordinator)
    {
        if (state.CritterPowerId == Guid.Empty)
        {
            throw new ArgumentException(
                "Critter Power Count editing requires a stable critter-power identity.",
                nameof(state));
        }

        _workspaceId = workspaceId;
        _contentRevision = contentRevision;
        _state = state;
        string targetToken = state.CritterPowerId.ToString("N");
        Title = "Critter Power Count";
        AutomationId = $"critter-power-count-page-{targetToken}";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Selected critter power"));
        body.Add(NativeTheme.Title(string.IsNullOrEmpty(critterPowerName) ? "Critter Power" : critterPowerName));
        body.Add(NativeTheme.Body(
            "Match Chummer5's Counts towards Critter Power limit checkbox for this exact saved power.",
            NativeTheme.Muted));

        _countsTowardsLimit = new Switch
        {
            IsToggled = state.CountsTowardsLimit,
            AutomationId = $"critter-power-count-toggle-{targetToken}",
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
        row.Add(NativeTheme.FieldLabel("Counts towards Critter Power limit"), 0, 0);
        row.Add(_countsTowardsLimit, 1, 0);
        body.Add(NativeTheme.Card(row));

        _save = NativeTheme.PrimaryButton("Save Count State");
        _save.AutomationId = $"critter-power-count-save-{targetToken}";
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
        _countsTowardsLimit.IsEnabled = revisionMatches;
        _save.IsEnabled = revisionMatches;
    }

    private async Task SaveAsync()
    {
        if (_countsTowardsLimit.IsToggled == _state.CountsTowardsLimit)
        {
            await Navigation.PopAsync();
            return;
        }

        await Coordinator.ApplyCritterPowerCountEditAsync(new CritterPowerCountEditRequest(
            _workspaceId,
            _contentRevision,
            _state.CritterPowerId,
            _countsTowardsLimit.IsToggled));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
