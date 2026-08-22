using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class WeaponStolenPage : NativePageBase
{
    private sealed record NodeOption(CharacterWeaponStolenState State, string Label);

    private readonly WeaponStolenEditorState _editor;
    private readonly IReadOnlyList<NodeOption> _options;
    private readonly Picker _target;
    private readonly Switch _stolen;
    private readonly Button _save;

    public WeaponStolenPage(
        RunnerSessionCoordinator coordinator,
        WeaponStolenEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        if (editor.RootWeaponId == Guid.Empty
            || editor.Nodes.Count == 0
            || editor.Nodes.Any(node =>
                !CharacterWeaponStolenRules.IsValidIdentity(node.Identity)
                || node.Identity.Path[0].Id != editor.RootWeaponId
                || node.Phase != CharacterWeaponStolenPhase.Creation
                || node.Economics is not { NuyenDelta: 0m, KarmaDelta: 0 }))
        {
            throw new ArgumentException(
                "Weapon Stolen editing requires exact zero-economic creation nodes under one stable root Weapon.",
                nameof(editor));
        }

        string rootToken = editor.RootWeaponId.ToString("N");
        Title = "Weapon Stolen";
        AutomationId = $"weapon-stolen-page-{rootToken}";
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Creation Weapon tree"));
        body.Add(NativeTheme.Title("Stolen"));
        body.Add(NativeTheme.Body(
            "Choose the exact saved Weapon, accessory, or accessory Gear node. This changes creation cost partitioning without a Nuyen or Karma transaction.",
            NativeTheme.Muted));

        _options = editor.Nodes.Select(node => new NodeOption(
            node,
            $"{node.DisplayPath} · {node.Identity.Path[^1].Id.ToString("N")[..8]}"))
            .ToArray();
        _target = new Picker
        {
            Title = "Weapon-tree node",
            ItemsSource = _options,
            ItemDisplayBinding = new Binding(nameof(NodeOption.Label)),
            SelectedIndex = 0,
            AutomationId = $"weapon-stolen-target-{rootToken}",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _target.SelectedIndexChanged += (_, _) => LoadSelectedState();
        body.Add(NativeTheme.FieldLabel("Selected item"));
        body.Add(_target);

        _stolen = new Switch
        {
            AutomationId = $"weapon-stolen-toggle-{rootToken}",
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
        _save.AutomationId = $"weapon-stolen-save-{rootToken}";
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
                "Item required",
                "Choose one exact saved Weapon-tree node before saving.",
                "OK");
            return;
        }

        CharacterWeaponStolenState selected = _options[_target.SelectedIndex].State;
        if (_stolen.IsToggled == selected.Stolen)
        {
            await Navigation.PopAsync();
            return;
        }

        await Coordinator.ApplyWeaponStolenEditAsync(new WeaponStolenEditRequest(
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
