using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class GearStolenPage : NativePageBase
{
    private sealed record NodeOption(CharacterGearStolenState State, string Label);

    private readonly GearStolenEditorState _editor;
    private readonly IReadOnlyList<NodeOption> _options;
    private readonly Picker _target;
    private readonly Switch _stolen;
    private readonly Button _save;

    public GearStolenPage(
        RunnerSessionCoordinator coordinator,
        GearStolenEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        if (editor.RootGearId == Guid.Empty
            || editor.Nodes.Count == 0
            || editor.Nodes.Any(node =>
                !CharacterGearStolenRules.IsValidIdentity(node.Identity)
                || node.Identity.GearPath[0] != editor.RootGearId))
        {
            throw new ArgumentException(
                "Gear Stolen editing requires exact nodes under one stable root Gear.",
                nameof(editor));
        }

        string rootToken = editor.RootGearId.ToString("N");
        Title = "Gear Stolen";
        AutomationId = $"gear-stolen-page-{rootToken}";
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Selected Gear tree"));
        body.Add(NativeTheme.Title("Stolen"));
        body.Add(NativeTheme.Body(
            "Choose the exact saved Gear node represented by Chummer5's recursive Gear tree.",
            NativeTheme.Muted));

        _options = editor.Nodes.Select(node => new NodeOption(
            node,
            $"{node.DisplayPath} · {node.Identity.GearPath[^1].ToString("N")[..8]}"))
            .ToArray();
        _target = new Picker
        {
            Title = "Gear node",
            ItemsSource = _options,
            ItemDisplayBinding = new Binding(nameof(NodeOption.Label)),
            SelectedIndex = 0,
            AutomationId = $"gear-stolen-target-{rootToken}",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _target.SelectedIndexChanged += (_, _) => LoadSelectedState();
        body.Add(NativeTheme.FieldLabel("Selected Gear"));
        body.Add(_target);

        _stolen = new Switch
        {
            AutomationId = $"gear-stolen-toggle-{rootToken}",
            OnColor = NativeTheme.Signal
        };
        SemanticProperties.SetDescription(_stolen, "Stolen");
        Grid row = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        row.Add(NativeTheme.FieldLabel("Stolen"), 0, 0);
        row.Add(_stolen, 1, 0);
        body.Add(NativeTheme.Card(row));

        _save = NativeTheme.PrimaryButton("Save Stolen state");
        _save.AutomationId = $"gear-stolen-save-{rootToken}";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);
        Content = new ScrollView { Content = body };
        LoadSelectedState();
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private void LoadSelectedState()
    {
        if (_target.SelectedIndex >= 0 && _target.SelectedIndex < _options.Count)
        {
            _stolen.IsToggled = _options[_target.SelectedIndex].State.Stolen;
        }
    }

    private void RefreshEnabledState()
    {
        bool canEdit = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        _target.IsEnabled = canEdit;
        _stolen.IsEnabled = canEdit;
        _save.IsEnabled = canEdit;
    }

    private async Task SaveAsync()
    {
        if (_target.SelectedIndex < 0 || _target.SelectedIndex >= _options.Count)
        {
            await DisplayAlertAsync(
                "Gear required",
                "Choose one exact saved Gear node before saving.",
                "OK");
            return;
        }

        CharacterGearStolenState selected = _options[_target.SelectedIndex].State;
        if (_stolen.IsToggled == selected.Stolen)
        {
            await Navigation.PopAsync();
            return;
        }

        await Coordinator.ApplyGearStolenEditAsync(new GearStolenEditRequest(
            _editor.WorkspaceId,
            _editor.ContentRevision,
            selected.Identity,
            selected.Revision,
            _stolen.IsToggled));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
