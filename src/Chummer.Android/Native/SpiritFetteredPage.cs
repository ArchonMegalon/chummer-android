using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class SpiritFetteredPage : NativePageBase
{
    private readonly CharacterWorkspaceId _workspaceId;
    private readonly long _contentRevision;
    private readonly CharacterSpiritFetteringState _state;
    private readonly Switch _fettered;
    private readonly Button _save;

    public SpiritFetteredPage(
        RunnerSessionCoordinator coordinator,
        CharacterWorkspaceId workspaceId,
        long contentRevision,
        string displayName,
        CharacterSpiritFetteringState state) : base(coordinator)
    {
        if (state.SpiritId == Guid.Empty || state.EntityType is not ("Spirit" or "Sprite"))
        {
            throw new ArgumentException(
                "Fettered/Pet editing requires one stable Spirit or Sprite identity.",
                nameof(state));
        }

        _workspaceId = workspaceId;
        _contentRevision = contentRevision;
        _state = state;
        string targetToken = state.SpiritId.ToString("N");
        bool sprite = state.EntityType == "Sprite";
        string controlLabel = sprite ? "Pet" : "Fettered";
        Title = sprite ? "Sprite Pet" : "Fettered Spirit";
        AutomationId = $"spirit-fettered-page-{targetToken}";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow($"Selected {state.EntityType.ToLowerInvariant()}"));
        body.Add(NativeTheme.Title(string.IsNullOrWhiteSpace(displayName) ? state.EntityType : displayName));
        body.Add(NativeTheme.Body(
            sprite
                ? "Match Chummer5's Sprite Pet checkbox. The control is enabled only when the saved runner proves the Sprite Pet improvement."
                : "Match Chummer5's Fettered checkbox, including its one-Fettered/Pet entity and Career unbound limits.",
            NativeTheme.Muted));

        _fettered = new Switch
        {
            IsToggled = state.Fettered,
            AutomationId = $"spirit-fettered-toggle-{targetToken}",
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
        row.Add(NativeTheme.FieldLabel(controlLabel), 0, 0);
        row.Add(_fettered, 1, 0);
        body.Add(NativeTheme.Card(row));

        if (!state.Fettered && state.Created && state.ActivationCostExact)
        {
            body.Add(NativeTheme.Body(
                $"Fettering spends {state.ActivationKarmaCost.ToString(CultureInfo.InvariantCulture)} Karma; "
                + $"{state.AvailableKarma.ToString(CultureInfo.InvariantCulture)} available.",
                NativeTheme.Muted));
        }
        else if (!state.Fettered && state.Created && !state.ActivationCostExact)
        {
            body.Add(NativeTheme.Body(
                "Read-only: the active KarmaSpiritFettering setting is not persisted with this runner.",
                NativeTheme.Danger));
        }

        string saveLabel = !state.Fettered && state.Created && state.ActivationCostExact
            ? $"Spend {state.ActivationKarmaCost.ToString(CultureInfo.InvariantCulture)} Karma & Save"
            : "Save Fettered/Pet State";
        _save = NativeTheme.PrimaryButton(saveLabel);
        _save.AutomationId = $"spirit-fettered-save-{targetToken}";
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
        bool directionEnabled = _state.Fettered ? _state.CanUnfetter : _state.CanFetter;
        _fettered.IsEnabled = revisionMatches && directionEnabled;
        _save.IsEnabled = revisionMatches && directionEnabled;
    }

    private async Task SaveAsync()
    {
        if (_fettered.IsToggled == _state.Fettered)
        {
            await Navigation.PopAsync();
            return;
        }

        await Coordinator.ApplySpiritFetteredEditAsync(new SpiritFetteredEditRequest(
            _workspaceId,
            _contentRevision,
            _state,
            _fettered.IsToggled));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
