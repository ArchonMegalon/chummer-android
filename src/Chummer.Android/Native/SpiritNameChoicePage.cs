using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class SpiritNameChoicePage : NativePageBase
{
    private readonly CharacterWorkspaceId _workspaceId;
    private readonly long _contentRevision;
    private readonly CharacterSpiritNameChoiceState _state;
    private readonly Picker _name;
    private readonly Button _save;

    public SpiritNameChoicePage(
        RunnerSessionCoordinator coordinator,
        CharacterWorkspaceId workspaceId,
        long contentRevision,
        string displayName,
        CharacterSpiritNameChoiceState state) : base(coordinator)
    {
        if (!CharacterSpiritNameChoiceRules.IsValidState(state))
        {
            throw new ArgumentException(
                "Spirit/Sprite metatype editing requires exact typed selector state.",
                nameof(state));
        }

        _workspaceId = workspaceId;
        _contentRevision = contentRevision;
        _state = state;
        string token = state.SpiritId.ToString("N");
        Title = $"{state.EntityType} metatype";
        AutomationId = $"spirit-name-choice-page-{token}";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow($"Selected {state.EntityType.ToLowerInvariant()}"));
        body.Add(NativeTheme.Title(
            string.IsNullOrWhiteSpace(displayName) ? state.EntityType : displayName));
        body.Add(NativeTheme.Body(
            "Choose from the exact Chummer5 tradition/stream DropDownList. This changes the "
            + "Spirit/Sprite metatype, not its personal name or linked runner.",
            NativeTheme.Muted));
        body.Add(NativeTheme.FieldLabel("Spirit/Sprite metatype"));

        string[] options = state.AllowedNames.ToArray();
        _name = new Picker
        {
            Title = "Spirit/Sprite metatype",
            ItemsSource = options,
            SelectedItem = options.FirstOrDefault(value => string.Equals(
                value,
                state.CurrentName,
                StringComparison.Ordinal)),
            AutomationId = $"spirit-name-choice-picker-{token}",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _name.SelectedIndexChanged += (_, _) => RefreshEnabledState();
        body.Add(_name);

        _save = NativeTheme.PrimaryButton("Save Spirit/Sprite metatype");
        _save.AutomationId = $"spirit-name-choice-save-{token}";
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
        bool validChoice = _name.SelectedItem is string selected
            && CharacterSpiritNameChoiceRules.CanSet(_state, selected);
        _name.IsEnabled = revisionMatches;
        _save.IsEnabled = revisionMatches && validChoice;
    }

    private async Task SaveAsync()
    {
        if (_name.SelectedItem is not string selected
            || !CharacterSpiritNameChoiceRules.CanSet(_state, selected))
        {
            await DisplayAlertAsync(
                "Spirit/Sprite metatype unavailable",
                "Choose a value from the exact Chummer5 tradition/stream list.",
                "OK");
            return;
        }
        if (string.Equals(selected, _state.CurrentName, StringComparison.Ordinal))
        {
            await Navigation.PopAsync();
            return;
        }

        await Coordinator.ApplySpiritNameChoiceEditAsync(new SpiritNameChoiceEditRequest(
            _workspaceId,
            _contentRevision,
            _state,
            selected));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
