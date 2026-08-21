using System.Globalization;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class QualityLevelPage : NativePageBase
{
    private readonly CharacterWorkspaceId _workspaceId;
    private readonly long _contentRevision;
    private readonly WorkspaceQualityLevelState _state;
    private readonly Entry _level;
    private readonly Button _save;

    public QualityLevelPage(
        RunnerSessionCoordinator coordinator,
        CharacterWorkspaceId workspaceId,
        long contentRevision,
        string qualityName,
        WorkspaceQualityLevelState state) : base(coordinator)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.QualityId == Guid.Empty || state.Level < 1 || state.MaximumLevel < state.Level)
        {
            throw new ArgumentException("Quality Level editing requires exact stable level semantics.", nameof(state));
        }

        _workspaceId = workspaceId;
        _contentRevision = contentRevision;
        _state = state;
        string token = state.QualityId.ToString("N");
        Title = "Quality Level";
        AutomationId = $"quality-level-page-{token}";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow(state.CareerMode ? "Career quality" : "Creation quality"));
        body.Add(NativeTheme.Title(string.IsNullOrWhiteSpace(qualityName) ? "Quality" : qualityName));
        Label currentLevel = NativeTheme.Body(
            $"The legacy level identity currently contains {state.Level.ToString(CultureInfo.InvariantCulture)} saved instance(s); "
            + $"this source allows 1–{state.MaximumLevel.ToString(CultureInfo.InvariantCulture)}.",
            NativeTheme.Muted);
        currentLevel.AutomationId = $"quality-level-current-{token}";
        body.Add(currentLevel);

        _level = NativeTheme.TextField(
            $"quality-level-value-{token}",
            state.Level.ToString(CultureInfo.InvariantCulture));
        _level.Keyboard = Keyboard.Numeric;
        body.Add(NativeTheme.FieldLabel("Level"));
        body.Add(_level);

        _save = NativeTheme.PrimaryButton("Save Quality Level");
        _save.AutomationId = $"quality-level-save-{token}";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);

        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private void RefreshEnabledState()
    {
        bool current = Coordinator.State.WorkspaceId == _workspaceId
            && Coordinator.State.ContentRevision == _contentRevision;
        _level.IsEnabled = current;
        _save.IsEnabled = current;
    }

    private async Task SaveAsync()
    {
        if (!int.TryParse(_level.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int newLevel)
            || newLevel < 1
            || newLevel > _state.MaximumLevel)
        {
            throw new InvalidOperationException(
                $"Quality Level must be between 1 and {_state.MaximumLevel.ToString(CultureInfo.InvariantCulture)}.");
        }
        if (newLevel == _state.Level)
        {
            await Navigation.PopAsync();
            return;
        }

        bool confirmed = true;
        if (_state.CareerMode && newLevel > _state.Level)
        {
            confirmed = await DisplayAlertAsync(
                "Confirm Quality Level increase",
                $"Add {newLevel - _state.Level} {_state.QualityType.ToLowerInvariant()} quality level(s)? "
                + "This bounded lane is available only for exact free, side-effect-free saved qualities.",
                "Increase",
                "Cancel");
            if (!confirmed)
            {
                return;
            }
        }

        await Coordinator.ApplyQualityLevelEditAsync(new QualityLevelEditRequest(
            _workspaceId,
            _contentRevision,
            _state.QualityId,
            _state.Level,
            _state.MaximumLevel,
            newLevel,
            confirmed));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
