using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class GearEquipmentPage : NativePageBase
{
    private sealed record NodeOption(CharacterGearEquipmentState State, string Label);

    private readonly GearEquipmentEditorState _editor;
    private readonly IReadOnlyList<NodeOption> _options;
    private readonly Picker _target;
    private readonly Switch _equipped;
    private readonly Button _save;

    public GearEquipmentPage(
        RunnerSessionCoordinator coordinator,
        GearEquipmentEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        if (editor.RootGearId == Guid.Empty
            || editor.Nodes.Count == 0
            || editor.Nodes.Any(node =>
                !CharacterGearEquipmentRules.IsValidIdentity(node.Identity)
                || node.Identity.GearPath[0] != editor.RootGearId
                || node.Economics is not { NuyenDelta: 0m, KarmaDelta: 0 }))
        {
            throw new ArgumentException(
                "Gear Equipped editing requires exact zero-economic nodes under one stable root Gear.",
                nameof(editor));
        }

        string rootToken = editor.RootGearId.ToString("N");
        Title = "Gear Equipped";
        AutomationId = $"gear-equipment-page-{rootToken}";
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Selected Gear tree"));
        body.Add(NativeTheme.Title("Equipped"));
        body.Add(NativeTheme.Body(
            "Choose the exact saved Gear node. This Create/Career state change has no Nuyen or Karma cost.",
            NativeTheme.Muted));

        _options = editor.Nodes.Select(node => new NodeOption(
            node,
            $"{node.DisplayPath} · {node.Identity.GearPath[^1].ToString("N")[..8]}"))
            .ToArray();
        _target = new Picker
        {
            Title = "Gear node",
            ItemsSource = (System.Collections.IList)_options,
            ItemDisplayBinding = new Binding(nameof(NodeOption.Label)),
            SelectedIndex = 0,
            AutomationId = $"gear-equipment-target-{rootToken}",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _target.SelectedIndexChanged += (_, _) => LoadSelectedState();
        body.Add(NativeTheme.FieldLabel("Selected Gear"));
        body.Add(_target);

        _equipped = new Switch
        {
            AutomationId = $"gear-equipment-toggle-{rootToken}",
            OnColor = NativeTheme.Signal
        };
        SemanticProperties.SetDescription(_equipped, "Equipped");
        Grid row = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        row.Add(NativeTheme.FieldLabel("Equipped"), 0, 0);
        row.Add(_equipped, 1, 0);
        body.Add(NativeTheme.Card(row));

        _save = NativeTheme.PrimaryButton("Save Equipped state");
        _save.AutomationId = $"gear-equipment-save-{rootToken}";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);
        Content = new ScrollView { Content = body };
        LoadSelectedState();
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private CharacterGearEquipmentState? SelectedState
        => _target.SelectedIndex >= 0 && _target.SelectedIndex < _options.Count
            ? _options[_target.SelectedIndex].State
            : null;

    private void LoadSelectedState()
    {
        if (SelectedState is { } selected)
        {
            _equipped.IsToggled = selected.Equipped;
        }
        RefreshEnabledState();
    }

    private void RefreshEnabledState()
    {
        bool current = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        _target.IsEnabled = current;
        _equipped.IsEnabled = current && SelectedState?.CanChangeEquip == true;
        _save.IsEnabled = current && SelectedState?.CanChangeEquip == true;
    }

    private async Task SaveAsync()
    {
        if (SelectedState is not { } selected)
        {
            await DisplayAlertAsync(
                "Gear required",
                "Choose one exact saved Gear node before saving.",
                "OK");
            return;
        }
        if (!selected.CanChangeEquip)
        {
            await DisplayAlertAsync(
                "Equipped state is fixed",
                "Included or clip-loaded Gear cannot change equipped state.",
                "OK");
            return;
        }
        if (_equipped.IsToggled == selected.Equipped)
        {
            await Navigation.PopAsync();
            return;
        }

        await Coordinator.ApplyGearEquipmentEditAsync(new GearEquipmentEditRequest(
            _editor.WorkspaceId,
            _editor.ContentRevision,
            selected.Identity,
            selected.Revision,
            _equipped.IsToggled));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
